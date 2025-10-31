using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftBookingTests
{
    private static (StatementDraftService sut, AppDbContext db, SqliteConnection conn, Guid owner) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var ownerUser = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(ownerUser);
        db.SaveChanges();
        // ensure self contact exists
        var self = new Contact(ownerUser.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        db.SaveChanges();
        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, conn, ownerUser.Id);
    }

    private static async Task<(Account account, Contact bank)> AddAccountAsync(AppDbContext db, Guid owner, AccountType type = AccountType.Giro)
    {
        var bank = new Contact(owner, "Bank", ContactType.Bank, null, null);
        db.Contacts.Add(bank);
        await db.SaveChangesAsync();
        var acc = new Account(owner, type, "Testkonto", "DE00", bank.Id);
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        return (acc, bank);
    }

    private static async Task<StatementDraft> CreateDraftAsync(AppDbContext db, Guid owner, Guid? accountId = null)
    {
        var draft = new StatementDraft(owner, "file.csv", "", "");
        if (accountId != null)
        {
            draft.SetDetectedAccount(accountId.Value);
        }
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task Booking_SingleEntry_ShouldNotCommitWholeDraft_And_RemoveOnlyThatEntry()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        // normal recipient contact (no intermediary, no self)
        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync();

        // two entries
        var e1 = draft.AddEntry(DateTime.Today, 10m, "A", shop.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, 20m, "B", shop.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added;
        db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(shop.Id); e2.MarkAccounted(shop.Id);
        await db.SaveChangesAsync();

        // IMPORTANT: simulate production by using a fresh DbContext (new scope)
        var freshOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        using var freshDb = new AppDbContext(freshOptions);
        var freshSut = new StatementDraftService(freshDb, new PostingAggregateService(freshDb));

        // Act: book only first entry on fresh context
        var res = await freshSut.BookAsync(draft.Id, e1.Id, owner, false, CancellationToken.None);

        // Assert
        res.Success.Should().BeTrue();

        // Reload draft and verify status and remaining entries using fresh context
        var reloaded = await freshDb.StatementDrafts.Include(d => d.Entries).FirstAsync(d => d.Id == draft.Id);
        reloaded.Status.Should().Be(StatementDraftStatus.Draft); // not committed because one entry remains
        reloaded.Entries.Should().HaveCount(1);
        reloaded.Entries.Single().Subject.Should().Be("B");

        // Exactly two postings (bank + contact) for the booked entry
        freshDb.Postings.Count().Should().Be(2);
        freshDb.Postings.Count(p => p.Kind == PostingKind.Bank).Should().Be(1);
        freshDb.Postings.Count(p => p.Kind == PostingKind.Contact).Should().Be(1);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_ShouldFail_WhenNoAccountAssigned()
    {
        var (sut, db, conn, owner) = Create();
        var draft = await CreateDraftAsync(db, owner, null);
        var entry = draft.AddEntry(DateTime.Today, 10m, "Payment A", null, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "NO_ACCOUNT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_ShouldFail_WhenEntryHasNoContact()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var entry = draft.AddEntry(DateTime.Today, 10m, "Payment A", null, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "ENTRY_NO_CONTACT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContact_ShouldRequireConfirmation_AndCreateBankAndContactPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var entry = draft.AddEntry(DateTime.Today, 25.5m, "Self transfer", self.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(self.Id);
        await db.SaveChangesAsync();

        var res1 = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res1.Success.Should().BeFalse();
        res1.HasWarnings.Should().BeTrue();
        res1.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_MISSING_FOR_SELF").Should().BeTrue();

        var res2 = await sut.BookAsync(draft.Id, null,owner, true, CancellationToken.None);
        res2.Success.Should().BeTrue();
        db.Postings.Count().Should().Be(2);
        db.Postings.Count(p => p.Kind == PostingKind.Bank).Should().Be(1);
        db.Postings.Count(p => p.Kind == PostingKind.Contact).Should().Be(1);
        // Aggregates created for month/quarter/halfyear/year for account and contact
        // New behavior: aggregates created per DateKind (Booking + Valuta) so counts are doubled
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank).Should().Be(8);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact).Should().Be(8);
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContactWithSavingsPlan_ShouldCreateBankContactAndSavingsPostings()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan A", SavingsPlanType.OneTime, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();
        var e = draft.AddEntry(DateTime.Today, 100m, "Save", self.Name, DateTime.Today, "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        db.Postings.Count().Should().Be(3);
        db.Postings.Count(p => p.Kind == PostingKind.Bank).Should().Be(1);
        db.Postings.Count(p => p.Kind == PostingKind.Contact).Should().Be(1);
        db.Postings.Count(p => p.Kind == PostingKind.SavingsPlan).Should().Be(1);
        db.Postings.Single(p => p.Kind == PostingKind.SavingsPlan).Amount.Should().Be(-100m);
        // Aggregates exist for bank/contact/savingsplan (doubled due to DateKind)
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank).Should().Be(8);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact).Should().Be(8);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.SavingsPlan).Should().Be(8);
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_PaymentIntermediaryWithoutSplit_ShouldFail()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayPal", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var e = draft.AddEntry(DateTime.Today, 50m, "PayPal", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "INTERMEDIARY_NO_SPLIT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SplitDrafts_ParentCreatesZeroAndChildPostings_AndBothCommitted()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        // Parent draft with intermediary contact
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        // parent booking and valuta differ
        var pBooking = new DateTime(2024, 6, 1);
        var pValuta = new DateTime(2024, 6, 5);
        var pEntry = parent.AddEntry(pBooking, 80m, "Split Root", intermediary.Name, pValuta, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Child draft without account, two entries with assigned contacts totaling 80
        var child = await CreateDraftAsync(db, owner, null);
        var rec1 = new Contact(owner, "Alice", ContactType.Person, null, null);
        var rec2 = new Contact(owner, "Bob", ContactType.Person, null, null);
        db.Contacts.AddRange(rec1, rec2);
        await db.SaveChangesAsync();
        // children have different booking/valuta (and different from parent)
        var c1Booking = new DateTime(2024, 6, 2);
        var c1Valuta = new DateTime(2024, 6, 3);
        var c2Booking = new DateTime(2024, 6, 2);
        var c2Valuta = new DateTime(2024, 6, 4);
        var c1 = child.AddEntry(c1Booking, 30m, "Child A", rec1.Name, c1Valuta, "EUR", null, false);
        var c2 = child.AddEntry(c2Booking, 50m, "Child B", rec2.Name, c2Valuta, "EUR", null, false);
        c1.MarkAccounted(rec1.Id);
        c2.MarkAccounted(rec2.Id);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Link child as split draft to parent entry
        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        // Booking the child (split) draft should fail
        var childRes = await sut.BookAsync(child.Id, null, owner, false, CancellationToken.None);
        childRes.Success.Should().BeFalse();

        // Booking the parent should succeed and create 0-amount parent postings + child postings
        var parentRes = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        parentRes.Success.Should().BeTrue();

        var bankPostings = db.Postings.Where(p => p.Kind == PostingKind.Bank).ToList();
        var contactPostings = db.Postings.Where(p => p.Kind == PostingKind.Contact).ToList();

        bankPostings.Count.Should().Be(3); // 1 parent (0) + 2 child
        bankPostings.Count(p => p.Amount == 0m).Should().Be(1);
        bankPostings.Where(p => p.Amount != 0m).Sum(p => p.Amount).Should().Be(80m);

        contactPostings.Count.Should().Be(3); // 1 parent (0) + 2 child
        contactPostings.Count(p => p.Amount == 0m).Should().Be(1);
        contactPostings.Where(p => p.Amount != 0m).Sum(p => p.Amount).Should().Be(80m);

        // All created postings (parent + children) must have parent's valuta date
        var allPostings = db.Postings.ToList();
        allPostings.Should().OnlyContain(p => p.ValutaDate == pEntry.ValutaDate);

        // Additionally, booking dates of postings must match their source entry booking dates
        var parentBank = db.Postings.Single(p => p.SourceId == pEntry.Id && p.Kind == PostingKind.Bank);
        var parentContact = db.Postings.Single(p => p.SourceId == pEntry.Id && p.Kind == PostingKind.Contact);
        parentBank.BookingDate.Should().Be(pEntry.BookingDate);
        parentBank.ValutaDate.Should().Be(pEntry.ValutaDate);
        parentBank.Amount.Should().Be(0m);
        parentContact.BookingDate.Should().Be(pEntry.BookingDate);
        parentContact.ValutaDate.Should().Be(pEntry.ValutaDate);
        parentContact.Amount.Should().Be(0m);

        var child1Bank = db.Postings.Single(p => p.SourceId == c1.Id && p.Kind == PostingKind.Bank);
        var child1Contact = db.Postings.Single(p => p.SourceId == c1.Id && p.Kind == PostingKind.Contact);
        child1Bank.BookingDate.Should().Be(c1Booking);
        child1Bank.ValutaDate.Should().Be(pEntry.ValutaDate);
        child1Bank.Amount.Should().Be(c1.Amount);
        child1Contact.BookingDate.Should().Be(c1Booking);
        child1Contact.ValutaDate.Should().Be(pEntry.ValutaDate);
        child1Contact.Amount.Should().Be(c1.Amount);

        var child2Bank = db.Postings.Single(p => p.SourceId == c2.Id && p.Kind == PostingKind.Bank);
        var child2Contact = db.Postings.Single(p => p.SourceId == c2.Id && p.Kind == PostingKind.Contact);
        child2Bank.BookingDate.Should().Be(c2Booking);
        child2Bank.ValutaDate.Should().Be(pEntry.ValutaDate);
        child2Bank.Amount.Should().Be(c2.Amount);
        child2Contact.BookingDate.Should().Be(c2Booking);
        child2Contact.ValutaDate.Should().Be(pEntry.ValutaDate);
        child2Contact.Amount.Should().Be(c2.Amount);

        // Parent postings should not have parent set
        parentBank.ParentId.Should().BeNull();
        parentContact.ParentId.Should().BeNull();

        // Child postings must reference parent's corresponding posting ids
        child1Bank.ParentId.Should().Be(parentBank.Id);
        child1Contact.ParentId.Should().Be(parentContact.Id);
        child2Bank.ParentId.Should().Be(parentBank.Id);
        child2Contact.ParentId.Should().Be(parentContact.Id);

        // Both drafts expected committed
        (await db.StatementDrafts.FindAsync(parent.Id))!.Status.Should().Be(StatementDraftStatus.Committed);
        (await db.StatementDrafts.FindAsync(child.Id))!.Status.Should().Be(StatementDraftStatus.Committed);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_ParentFails_WhenSplitDraftHasMissingContacts()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 80m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 30m, "Child A", null, DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 50m, "Child B", null, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync();

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "INTERMEDIARY_NO_SPLIT").Should().BeFalse();
        res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "ENTRY_NO_CONTACT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_ParentFails_WhenSplitTotalsDoNotMatch()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 30m, "Child A", null, DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 60m, "Child B", null, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync();

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SPLIT_AMOUNT_MISMATCH").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Warns_WhenSplitContainsSelfContact()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        var child = await CreateDraftAsync(db, owner, null);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var c1 = child.AddEntry(DateTime.Today, 100m, "Child A", self.Name, DateTime.Today, "EUR", null, false);
        c1.MarkAccounted(self.Id);
        db.Entry(c1).State = EntityState.Added;
        await db.SaveChangesAsync();

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.HasWarnings.Should().BeTrue();
        res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "SAVINGSPLAN_MISSING_FOR_SELF").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Fails_WhenSplitContainsIntermediaryWithoutFurtherSplit()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 100m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var child = await CreateDraftAsync(db, owner, null);
        var c1 = child.AddEntry(DateTime.Today, 100m, "Child A", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        c1.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();
        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "INTERMEDIARY_NO_SPLIT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Fails_ForSecurityMissingTransactionType()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId);
        var entry = draft.AddEntry(DateTime.Today, 200m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, null, null, null, null, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SECURITY_MISSING_TXTYPE").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Fails_ForSecurityMissingQuantity()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId);
        var entry = draft.AddEntry(DateTime.Today, 200m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, null, null, null, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SECURITY_MISSING_QUANTITY").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Fails_WhenSecurityFeePlusTaxExceedsAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);

        // entry with amount smaller than fee+tax
        var entry = draft.AddEntry(DateTime.Today, 100m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Set security with Buy, quantity present, but fee+tax exceed entry amount
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 1.0m, 70.00m, 40.00m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SECURITY_FEE_TAX_EXCEEDS_AMOUNT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_CreatesSecurityTradeFeeTaxPostings_WithSixDecimalQuantity_AndSumsToEntryAmount_ForBuy()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(acc => acc.Id == draft.DetectedAccountId);
        var entry = draft.AddEntry(DateTime.Today, 1000m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF X", "DE000A0", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 1.123456m, 2.50m, 5.00m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        db.Postings.Count(p => p.Kind == PostingKind.Security).Should().Be(3);

        // Main security posting must be of subtype Buy
        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Buy);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);
        main.Amount.Should().Be(1000m - 2.50m - 5.00m);
        fee.Amount.Should().Be(2.50m);
        tax.Amount.Should().Be(5.00m);

        // Sum of security postings equals original entry amount
        (main.Amount + fee.Amount + tax.Amount).Should().Be(entry.Amount);

        // Quantity assertions
        main.Quantity.Should().Be(1.123456m);
        fee.Quantity.Should().BeNull();
        tax.Quantity.Should().BeNull();

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_CreatesSecurityPostings_ForSell_WithExpectedSigns_AndSumsToEntryAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);
        var entry = draft.AddEntry(DateTime.Today, 800m, "Sell", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF Y", "DE000B1", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        var feeAmt = 3.40m; var taxAmt = 7.60m;
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Sell, 5.0m, feeAmt, taxAmt, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        db.Postings.Count(p => p.Kind == PostingKind.Security).Should().Be(3);

        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Sell);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);

        // Sell: main = amount + fee + tax; fee/tax negative
        main.Amount.Should().Be(800m + feeAmt + taxAmt);
        fee.Amount.Should().Be(-feeAmt);
        tax.Amount.Should().Be(-taxAmt);
        (main.Amount + fee.Amount + tax.Amount).Should().Be(entry.Amount);

        // Quantity assertions: sell should be negative
        main.Quantity.Should().Be(-5.0m);
        fee.Quantity.Should().BeNull();
        tax.Quantity.Should().BeNull();

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_CreatesSecurityPostings_ForDividend_QuantityOptional_AndSumsToEntryAmount()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);
        var entry = draft.AddEntry(DateTime.Today, 123.45m, "Dividend", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF Z", "DE000C1", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        var feeAmt = 1.00m; var taxAmt = 0.50m;
        // Dividend: quantity can be null
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, feeAmt, taxAmt, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var main = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Dividend);
        var fee = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee);
        var tax = db.Postings.Single(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax);

        // Dividend: main = amount + fee + tax; fee/tax negative
        main.Amount.Should().Be(entry.Amount + feeAmt + taxAmt);
        fee.Amount.Should().Be(-feeAmt);
        tax.Amount.Should().Be(-taxAmt);
        (main.Amount + fee.Amount + tax.Amount).Should().Be(entry.Amount);

        // Quantity assertions: dividend should have no quantity
        main.Quantity.Should().BeNull();
        fee.Quantity.Should().BeNull();
        tax.Quantity.Should().BeNull();

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Dividend_WithTaxOnly_Net1188_ShouldCreateMain1356_AndTax168()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);

        // Entry: 11.88 EUR net dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), 11.88m, "Dividend", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync();

        // Security in USD
        var sec = new Security(owner, "US ETF", "US000123", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Assign as Dividend with only tax 1.68 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, 1.68m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        securityPosts.Count.Should().Be(2);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        main.Amount.Should().Be(13.56m);
        tax.Amount.Should().Be(-1.68m);
        (main.Amount + tax.Amount).Should().Be(entry.Amount);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Dividend_Storno_NegativeValues_ShouldCreateMainMinus184_TaxPlus022()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);

        // Entry: -1.62 EUR storno dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), -1.62m, "Dividend Storno", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync();

        // USD security
        var sec = new Security(owner, "US ETF", "US000XYZ", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Assign Dividend with tax -0.22 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, -0.22m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        securityPosts.Count.Should().Be(2);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        // Expect main -1.84; tax +0.22; total equals entry amount
        main.Amount.Should().Be(-1.84m);
        tax.Amount.Should().Be(0.22m);
        (main.Amount + tax.Amount).Should().Be(entry.Amount);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Dividend_PositiveValues_ShouldCreateMainPlus184_TaxMinus022()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);

        // Entry: +1.62 EUR dividend
        var entry = draft.AddEntry(new DateTime(2024, 5, 10), 1.62m, "Dividend", bank.Name, new DateTime(2024, 5, 10), "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync();

        // USD security
        var sec = new Security(owner, "US ETF", "US000XYZ", null, null, "USD", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Assign Dividend with tax +0.22 EUR
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Dividend, null, null, 0.22m, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var securityPosts = db.Postings.Where(p => p.Kind == PostingKind.Security).ToList();
        securityPosts.Count.Should().Be(2);

        var main = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Dividend);
        var tax = securityPosts.Single(p => p.SecuritySubType == SecurityPostingSubType.Tax);

        // Expect main +1.84; tax -0.22; total equals entry amount
        main.Amount.Should().Be(1.84m);
        tax.Amount.Should().Be(-0.22m);
        (main.Amount + tax.Amount).Should().Be(entry.Amount);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_MonthEnd_31Jan_To_29Feb()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 31), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        var e = draft.AddEntry(new DateTime(2024, 1, 31), 100m, "Save", self.Name, new DateTime(2024, 1, 31), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id);
        updatedPlan.TargetDate!.Value.Date.Should().Be(new DateTime(2024, 2, 29));

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_NonMonthEnd_30Jan_To_29Feb_Capped()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 30), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        var e = draft.AddEntry(new DateTime(2024, 1, 30), 100m, "Save", self.Name, new DateTime(2024, 1, 30), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id);
        updatedPlan.TargetDate!.Value.Date.Should().Be(new DateTime(2024, 2, 29));

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContactWithRecurringSavingsPlan_MultipleAdvance_To_MarchEnd()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

        var plan = new SavingsPlan(owner, "Recurring", SavingsPlanType.Recurring, 1000m, new DateTime(2024, 1, 31), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // Booking on March 15th should advance Jan 31 -> Feb 29 -> Mar 31 (stop because > March 15)
        var e = draft.AddEntry(new DateTime(2024, 3, 15), 100m, "Save", self.Name, new DateTime(2024, 3, 15), "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var updatedPlan = await db.SavingsPlans.FirstAsync(p => p.Id == plan.Id);
        updatedPlan.TargetDate!.Value.Date.Should().Be(new DateTime(2024, 3, 31));

        conn.Dispose();
    }
}
