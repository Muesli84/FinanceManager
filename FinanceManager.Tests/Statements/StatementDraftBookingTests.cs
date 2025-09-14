using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
        var sut = new StatementDraftService(db);
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
        var draft = new StatementDraft(owner, "file.csv", "");
        if (accountId != null)
        {
            draft.SetDetectedAccount(accountId.Value);
        }
        db.StatementDrafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task Booking_SingleEntry_ShouldNotCommitWholeDraft_AndRemoveOnlyThatEntry()
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
        var freshSut = new StatementDraftService(freshDb);

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
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank).Should().Be(4);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact).Should().Be(4);
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
        // Aggregates exist for bank/contact/savingsplan
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank).Should().Be(4);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Contact).Should().Be(4);
        db.PostingAggregates.Count(a => a.Kind == PostingKind.SavingsPlan).Should().Be(4);
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
        var pEntry = parent.AddEntry(DateTime.Today, 80m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        pEntry.MarkAccounted(intermediary.Id);
        db.Entry(pEntry).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Child draft without account, two entries with assigned contacts totaling 80
        var child = await CreateDraftAsync(db, owner, null);
        var rec1 = new Contact(owner, "Alice", ContactType.Person, null, null);
        var rec2 = new Contact(owner, "Bob", ContactType.Person, null, null);
        db.Contacts.AddRange(rec1, rec2);
        await db.SaveChangesAsync();
        var c1 = child.AddEntry(DateTime.Today, 30m, "Child A", rec1.Name, DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 50m, "Child B", rec2.Name, DateTime.Today, "EUR", null, false);
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

        // Both drafts expected committed (current implementation likely only commits parent -> test will fail until implemented)
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
    public async Task Booking_CreatesSecurityTradeFeeTaxPostings_WithSixDecimalQuantity()
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
        // Trade amount = 1000 - 2.50 - 5.00 = 992.50
        db.Postings.Count(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Trade && p.Amount == 992.50m).Should().Be(1);
        db.Postings.Count(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Fee && p.Amount == 2.50m).Should().Be(1);
        db.Postings.Count(p => p.Kind == PostingKind.Security && p.SecuritySubType == SecurityPostingSubType.Tax && p.Amount == 5.00m).Should().Be(1);
        conn.Dispose();
    }

    [Fact]
    public async Task AssigningSameSplitDraftToMultipleEntries_ShouldFail()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var e1 = parent.AddEntry(DateTime.Today, 10m, "A", intermediary.Name, DateTime.Today, "EUR", null, false);
        var e2 = parent.AddEntry(DateTime.Today, 20m, "B", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added;
        db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(intermediary.Id);
        e2.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var split = await CreateDraftAsync(db, owner, null);

        // first assignment ok
        await sut.SetEntrySplitDraftAsync(parent.Id, e1.Id, split.Id, owner, CancellationToken.None);
        // second should throw
        Func<Task> act = () => sut.SetEntrySplitDraftAsync(parent.Id, e2.Id, split.Id, owner, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Fails_ForSecurityInvalidContact()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId);

        // Entry initially with bank as contact to allow setting security
        var entry = draft.AddEntry(DateTime.Today, 500m, "Trade", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(entry).State = EntityState.Added;
        entry.MarkAccounted(account!.BankContactId);
        await db.SaveChangesAsync();

        var sec = new Security(owner, "ETF Y", "DE000B1", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Set valid security data
        await sut.SetEntrySecurityAsync(draft.Id, entry.Id, sec.Id, SecurityTransactionType.Buy, 2.0m, 1.00m, 1.00m, owner, CancellationToken.None);

        // Change contact to a non-bank contact to invalidate
        var shop = new Contact(owner, "Shop GmbH", ContactType.Organization, null, null, false);
        db.Contacts.Add(shop);
        await db.SaveChangesAsync();
        await sut.SetEntryContactAsync(draft.Id, entry.Id, shop.Id, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SECURITY_INVALID_CONTACT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_ParentFails_WhenSplitDraftHasAccountAssigned()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);

        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var pEntry = parent.AddEntry(DateTime.Today, 75m, "Split Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        // Child draft with account (invalid for split)
        var child = await CreateDraftAsync(db, owner, acc.Id);
        var c1 = child.AddEntry(DateTime.Today, 25m, "Child A", "A", DateTime.Today, "EUR", null, false);
        var c2 = child.AddEntry(DateTime.Today, 50m, "Child B", "B", DateTime.Today, "EUR", null, false);
        db.Entry(c1).State = EntityState.Added;
        db.Entry(c2).State = EntityState.Added;
        await db.SaveChangesAsync();

        pEntry.AssignSplitDraft(child.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "SPLIT_DRAFT_HAS_ACCOUNT").Should().BeTrue();
        conn.Dispose();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Booking_SplitChain_CreatesPostingChains_AndCommitsAll(int depth)
    {
        // depth >= 2: top parent + (depth-2) intermediate + 1 leaf draft
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        // contacts
        var intermediary = new Contact(owner, "ChainPay", ContactType.Organization, null, null, true);
        var alice = new Contact(owner, "Alice", ContactType.Person, null, null);
        var bob = new Contact(owner, "Bob", ContactType.Person, null, null);
        db.Contacts.AddRange(intermediary, alice, bob);
        await db.SaveChangesAsync();

        // create top parent
        var drafts = new StatementDraft[depth];
        drafts[0] = await CreateDraftAsync(db, owner, acc.Id);
        var parentEntry = drafts[0].AddEntry(DateTime.Today, 100m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(parentEntry).State = EntityState.Added;
        parentEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        // build chain: intermediate drafts each with a single intermediary entry amount 100 -> split to next
        for (int i = 1; i < depth - 1; i++)
        {
            drafts[i] = await CreateDraftAsync(db, owner, null);
            var ie = drafts[i].AddEntry(DateTime.Today, 100m, $"Level {i}", intermediary.Name, DateTime.Today, "EUR", null, false);
            db.Entry(ie).State = EntityState.Added;
            ie.MarkAccounted(intermediary.Id);
            await db.SaveChangesAsync();
        }

        // leaf draft with concrete contacts totaling 100
        drafts[depth - 1] = await CreateDraftAsync(db, owner, null);
        var l1 = drafts[depth - 1].AddEntry(DateTime.Today, 40m, "Leaf A", alice.Name, DateTime.Today, "EUR", null, false);
        var l2 = drafts[depth - 1].AddEntry(DateTime.Today, 60m, "Leaf B", bob.Name, DateTime.Today, "EUR", null, false);
        db.Entry(l1).State = EntityState.Added;
        db.Entry(l2).State = EntityState.Added;
        l1.MarkAccounted(alice.Id);
        l2.MarkAccounted(bob.Id);
        await db.SaveChangesAsync();

        // link chain
        if (depth == 2)
        {
            parentEntry.AssignSplitDraft(drafts[1].Id);
        }
        else
        {
            parentEntry.AssignSplitDraft(drafts[1].Id);
            await db.SaveChangesAsync();
            for (int i = 1; i < depth - 1; i++)
            {
                var entry = await db.StatementDraftEntries.Where(e => e.DraftId == drafts[i].Id).SingleAsync();
                entry.AssignSplitDraft(drafts[i + 1].Id);
                await db.SaveChangesAsync();
            }
        }
        await db.SaveChangesAsync();

        // Act
        var res = await sut.BookAsync(drafts[0].Id, null, owner, false, CancellationToken.None);

        // Assert (intended behavior): booking succeeds, all drafts committed, and only leaf amounts are booked (no double-booking on intermediaries)
        res.Success.Should().BeTrue();
        for (int i = 0; i < depth; i++)
        {
            (await db.StatementDrafts.FindAsync(drafts[i].Id))!.Status.Should().Be(StatementDraftStatus.Committed);
        }

        var bankPostings = db.Postings.Where(p => p.Kind == PostingKind.Bank).ToList();
        var nonZero = bankPostings.Where(p => p.Amount != 0m).ToList();
        nonZero.Sum(p => p.Amount).Should().Be(100m);
        nonZero.Count.Should().Be(2); // expect only the two leaf entries to carry amounts (will currently fail for depth > 2)
        bankPostings.Count(p => p.Amount == 0m).Should().Be(depth - 1); // only the top-level parent 0 posting

        conn.Dispose();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task Booking_SplitChain_Fails_WhenBottomMissingContacts(int depth)
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        var intermediary = new Contact(owner, "ChainPay", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        var drafts = new StatementDraft[depth];
        drafts[0] = await CreateDraftAsync(db, owner, acc.Id);
        var p = drafts[0].AddEntry(DateTime.Today, 100m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(p).State = EntityState.Added;
        p.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        for (int i = 1; i < depth - 1; i++)
        {
            drafts[i] = await CreateDraftAsync(db, owner, null);
            var ie = drafts[i].AddEntry(DateTime.Today, 100m, $"Level {i}", intermediary.Name, DateTime.Today, "EUR", null, false);
            db.Entry(ie).State = EntityState.Added;
            ie.MarkAccounted(intermediary.Id);
            await db.SaveChangesAsync();
        }

        // bottom draft without contacts
        drafts[depth - 1] = await CreateDraftAsync(db, owner, null);
        var b1 = drafts[depth - 1].AddEntry(DateTime.Today, 40m, "Leaf A", null, DateTime.Today, "EUR", null, false);
        var b2 = drafts[depth - 1].AddEntry(DateTime.Today, 60m, "Leaf B", null, DateTime.Today, "EUR", null, false);
        db.Entry(b1).State = EntityState.Added;
        db.Entry(b2).State = EntityState.Added;
        await db.SaveChangesAsync();

        // link chain
        p.AssignSplitDraft(drafts[1].Id);
        await db.SaveChangesAsync();
        for (int i = 1; i < depth - 1; i++)
        {
            var ie = await db.StatementDraftEntries.Where(e => e.DraftId == drafts[i].Id).SingleAsync();
            ie.AssignSplitDraft(drafts[i + 1].Id);
            await db.SaveChangesAsync();
        }

        var res = await sut.BookAsync(drafts[0].Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Message.Contains("[Split]") && m.Code == "ENTRY_NO_CONTACT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SplitDrafts_DetectsCircularReference()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var intermediary = new Contact(owner, "ChainPay", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();

        // two drafts with circular split
        var d1 = await CreateDraftAsync(db, owner, acc.Id);
        var e1 = d1.AddEntry(DateTime.Today, 50m, "A", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added;
        e1.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var d2 = await CreateDraftAsync(db, owner, null);
        var e2 = d2.AddEntry(DateTime.Today, 50m, "B", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e2).State = EntityState.Added;
        e2.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var d3 = await CreateDraftAsync(db, owner, null);
        var e3 = d3.AddEntry(DateTime.Today, 50m, "B", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e3).State = EntityState.Added;
        e3.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        e1.AssignSplitDraft(d2.Id);
        await db.SaveChangesAsync();
        e2.AssignSplitDraft(d3.Id);
        await db.SaveChangesAsync();
        e3.AssignSplitDraft(d2.Id); // cycle
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => db.SaveChangesAsync());
        

        var res = await sut.BookAsync(d1.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SPLIT_CYCLE_DETECTED").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanGoalReached_WithExactSingleEntry_ShouldShowInformation_AndBookWithoutConfirmation()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var selfContact = await db.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan R", SavingsPlanType.OneTime, 100m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // existing accumulated = 60
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-5), 60m, null, null, null, null));
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e = draft.AddEntry(DateTime.Today, -40m, "Save", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(selfContact.Id);
        e.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var validation = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_REACHED_INFO" && m.Severity == "Information").Should().BeTrue();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanGoalReached_WithTwoEntriesSumExact_ShouldShowInformation_AndBookWithoutConfirmation_AndReturnInfo()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var selfContact = await db.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan R2", SavingsPlanType.OneTime, 200m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // existing accumulated = 150
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-10), 150m, null, null, null, null));
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e1 = draft.AddEntry(DateTime.Today, -25m, "Save1", bank.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, -25m, "Save2", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(selfContact.Id); e2.MarkAccounted(selfContact.Id);
        e1.AssignSavingsPlan(plan.Id); e2.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var validation = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_REACHED_INFO" && m.Severity == "Information").Should().BeTrue();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        // Booking result should include the same validation for UI display
        res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_REACHED_INFO").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanGoalExceeded_ShouldWarn_AndRequireConfirmation()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var selfContact = await db.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan W", SavingsPlanType.OneTime, 100m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // existing accumulated = 90
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-2), 90m, null, null, null, null));
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e1 = draft.AddEntry(DateTime.Today, -8m, "A", bank.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, -5m, "B", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(selfContact.Id); e2.MarkAccounted(selfContact.Id);
        e1.AssignSavingsPlan(plan.Id); e2.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var validation = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_EXCEEDED" && m.Severity == "Warning").Should().BeTrue();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.HasWarnings.Should().BeTrue();

        var forced = await sut.BookAsync(draft.Id, null,owner, true, CancellationToken.None);
        forced.Success.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanGoalReached_FromNegativeBalanceToZeroTarget_ShouldShowInformation_AndBookWithoutConfirmation_InResult()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var selfContact = await db.Contacts.FirstOrDefaultAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan Z", SavingsPlanType.OneTime, 0m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // existing accumulated = -30 (debt)
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-7), -30m, null, null, null, null));
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e1 = draft.AddEntry(DateTime.Today, -10m, "A", bank.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, -20m, "B", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(selfContact.Id); e2.MarkAccounted(selfContact.Id);
        e1.AssignSavingsPlan(plan.Id); e2.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var validation = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_REACHED_INFO" && m.Severity == "Information").Should().BeTrue();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_GOAL_REACHED_INFO").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Validate_SavingsPlanDue_WhenLatestBookingEqualsDueFriday_ShouldReportInformation()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        // create plan with target on last Friday
        DateTime today = DateTime.Today;
        int daysSinceFriday = ((int)today.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        var lastFriday = today.AddDays(-daysSinceFriday);
        var plan = new SavingsPlan(owner, "DuePlan", SavingsPlanType.OneTime, 200m, lastFriday, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        // some past postings, remaining > 0
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, lastFriday.AddMonths(-1), 50m, null, null, null, null));
        await db.SaveChangesAsync();

        // draft entries not assigned to plan; latest booking date = due date (Friday)
        var e1 = draft.AddEntry(lastFriday.AddDays(-2), 10m, "X", bank.Name, lastFriday.AddDays(-2), "EUR", null, false);
        var e2 = draft.AddEntry(lastFriday, 20m, "Y", bank.Name, lastFriday, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(bank.Id); e2.MarkAccounted(bank.Id);
        await db.SaveChangesAsync();

        var res = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        res.Messages.Any(m => m.Code == "SAVINGSPLAN_DUE" && m.Severity == "Information").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Validate_SavingsPlanDue_WhenLatestBookingBeforeDueFriday_ShouldNotReportInformation()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        DateTime today = DateTime.Today;
        int daysSinceFriday = ((int)today.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
        var lastFriday = today.AddDays(-daysSinceFriday);
        var lastThursday = lastFriday.AddDays(-1);
        var plan = new SavingsPlan(owner, "DuePlan2", SavingsPlanType.OneTime, 200m, lastFriday, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, lastFriday.AddMonths(-1), 50m, null, null, null, null));
        await db.SaveChangesAsync();

        var e1 = draft.AddEntry(lastThursday.AddDays(-5), 10m, "X", bank.Name, lastThursday.AddDays(-5), "EUR", null, false);
        var e2 = draft.AddEntry(lastThursday, 20m, "Y", bank.Name, lastThursday, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(bank.Id); e2.MarkAccounted(bank.Id);
        await db.SaveChangesAsync();

        var res = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        res.Messages.Any(m => m.Code == "SAVINGSPLAN_DUE").Should().BeFalse();
        conn.Dispose();
    }

    [Fact]
    public async Task Validate_SavingsPlanDue_WhenDueOnSunday_AndLatestBookingOnPreviousFriday_ShouldReportInformation()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);

        DateTime today = DateTime.Today;
        int daysSinceSunday = ((int)today.DayOfWeek - (int)DayOfWeek.Sunday + 7) % 7;
        var lastSunday = today.AddDays(-daysSinceSunday);
        var previousFriday = lastSunday.AddDays(-2);

        var plan = new SavingsPlan(owner, "DuePlan3", SavingsPlanType.OneTime, 200m, lastSunday, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, lastSunday.AddMonths(-1), 50m, null, null, null, null));
        await db.SaveChangesAsync();

        var e1 = draft.AddEntry(previousFriday.AddDays(-1), 10m, "X", bank.Name, previousFriday.AddDays(-1), "EUR", null, false);
        var e2 = draft.AddEntry(previousFriday, 20m, "Y", bank.Name, previousFriday, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(bank.Id); e2.MarkAccounted(bank.Id);
        await db.SaveChangesAsync();

        var res = await sut.ValidateAsync(draft.Id, null, owner, CancellationToken.None);
        res.Messages.Any(m => m.Code == "SAVINGSPLAN_DUE" && m.Severity == "Information").Should().BeTrue();
        conn.Dispose();
    }

    [Theory]
    [InlineData(SavingsPlanInterval.Monthly, 1)]
    [InlineData(SavingsPlanInterval.BiMonthly, 2)]
    [InlineData(SavingsPlanInterval.Quarterly, 3)]
    [InlineData(SavingsPlanInterval.SemiAnnually, 6)]
    [InlineData(SavingsPlanInterval.Annually, 12)]
    public async Task Booking_RecurringSavingsPlan_WhenDueToday_ExtendsTargetDate_ByInterval(SavingsPlanInterval interval, int months)
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var today = DateTime.Today;

        var plan = new SavingsPlan(owner, "R", SavingsPlanType.Recurring, 100m, today, interval);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e = draft.AddEntry(today, 10m, "Save", bank.Name, today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(self.Id);
        e.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var reloaded = await db.SavingsPlans.FindAsync(plan.Id);
        reloaded!.TargetDate!.Value.Date.Should().Be(today.AddMonths(months).Date);
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_RecurringSavingsPlan_WhenDueTomorrow_DoesNotChangeTargetDate()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var today = DateTime.Today;
        var tomorrow = today.AddDays(1);

        var plan = new SavingsPlan(owner, "R2", SavingsPlanType.Recurring, 100m, tomorrow, SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e = draft.AddEntry(today, 10m, "Save", bank.Name, today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(self.Id);
        e.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var reloaded = await db.SavingsPlans.FindAsync(plan.Id);
        reloaded!.TargetDate!.Value.Date.Should().Be(tomorrow.Date);
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_OneTimeSavingsPlan_WhenDueToday_DoesNotChangeTargetDate()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var today = DateTime.Today;

        var plan = new SavingsPlan(owner, "O", SavingsPlanType.OneTime, 100m, today, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();

        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var e = draft.AddEntry(today, 10m, "Save", bank.Name, today, "EUR", null, false);
        db.Entry(e).State = EntityState.Added;
        e.MarkAccounted(self.Id);
        e.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        var reloaded = await db.SavingsPlans.FindAsync(plan.Id);
        reloaded!.TargetDate!.Value.Date.Should().Be(today.Date);
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SelfContactWithSavingsPlan_OnSavingsAccount_ShouldFail()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner, AccountType.Savings);
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
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_INVALID_ACCOUNT").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanArchiveFlag_WithMismatch_ShouldFail()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

        // Plan with target 200, current 50 -> remaining 150; planned 70 -> mismatch
        var plan = new SavingsPlan(owner, "Archive Mismatch", SavingsPlanType.OneTime, 200m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-3), 50m, null, null, null, null));
        await db.SaveChangesAsync();

        var e1 = draft.AddEntry(DateTime.Today, 30m, "A", bank.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, 40m, "B", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(self.Id); e2.MarkAccounted(self.Id);
        e1.AssignSavingsPlan(plan.Id); e2.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        await sut.SetEntryArchiveSavingsPlanOnBookingAsync(draft.Id, e1.Id, true, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_ARCHIVE_MISMATCH").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_SavingsPlanArchiveFlag_WithExactSum_ShouldArchiveAndReturnInfo()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, bank) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);

        // Plan with target 200, current 150 -> remaining 50; planned 50 -> exact
        var plan = new SavingsPlan(owner, "Archive OK", SavingsPlanType.OneTime, 200m, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();
        db.Postings.Add(new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, plan.Id, null, DateTime.Today.AddDays(-4), 150m, null, null, null, null));
        await db.SaveChangesAsync();

        var e1 = draft.AddEntry(DateTime.Today, -20m, "A", bank.Name, DateTime.Today, "EUR", null, false);
        var e2 = draft.AddEntry(DateTime.Today, -30m, "B", bank.Name, DateTime.Today, "EUR", null, false);
        db.Entry(e1).State = EntityState.Added; db.Entry(e2).State = EntityState.Added;
        e1.MarkAccounted(self.Id); e2.MarkAccounted(self.Id);
        e1.AssignSavingsPlan(plan.Id); e2.AssignSavingsPlan(plan.Id);
        await db.SaveChangesAsync();

        await sut.SetEntryArchiveSavingsPlanOnBookingAsync(draft.Id, e1.Id, true, owner, CancellationToken.None);

        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();
        // Plan should be archived
        (await db.SavingsPlans.FindAsync(plan.Id))!.IsActive.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SAVINGSPLAN_ARCHIVED" && m.Severity == "Information").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_Aggregates_Update_WhenPreExisting()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var draft = await CreateDraftAsync(db, owner, acc.Id);
        var self = await db.Contacts.FirstAsync(c => c.OwnerUserId == owner && c.Type == ContactType.Self);
        var plan = new SavingsPlan(owner, "Plan A", SavingsPlanType.OneTime, null, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();
        var e = draft.AddEntry(DateTime.Today, 50m, "Save", self.Name, DateTime.Today, "EUR", null, false);
        e.AssignSavingsPlan(plan.Id);
        e.MarkAccounted(self.Id);
        db.Entry(e).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Pre-create aggregates with some amount
        var startMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        db.PostingAggregates.Add(new FinanceManager.Domain.Postings.PostingAggregate(PostingKind.Bank, acc.Id, null, null, null, startMonth, FinanceManager.Domain.Postings.AggregatePeriod.Month));
        db.PostingAggregates.Add(new FinanceManager.Domain.Postings.PostingAggregate(PostingKind.Contact, null, self.Id, null, null, startMonth, FinanceManager.Domain.Postings.AggregatePeriod.Month));
        db.PostingAggregates.Add(new FinanceManager.Domain.Postings.PostingAggregate(PostingKind.SavingsPlan, null, null, plan.Id, null, startMonth, FinanceManager.Domain.Postings.AggregatePeriod.Month));
        await db.SaveChangesAsync();

        // Act
        var res = await sut.BookAsync(draft.Id, null,owner, false, CancellationToken.None);
        res.Success.Should().BeTrue();

        // Assert: monthly aggregates updated to include the new amounts
        db.PostingAggregates.Count(a => a.Kind == PostingKind.Bank).Should().Be(4);
        db.PostingAggregates.Single(a => a.Kind == PostingKind.Bank && a.Period == FinanceManager.Domain.Postings.AggregatePeriod.Month && a.PeriodStart == startMonth).Amount.Should().Be(50m);
        db.PostingAggregates.Single(a => a.Kind == PostingKind.Contact && a.Period == FinanceManager.Domain.Postings.AggregatePeriod.Month && a.PeriodStart == startMonth).Amount.Should().Be(50m);
        db.PostingAggregates.Single(a => a.Kind == PostingKind.SavingsPlan && a.Period == FinanceManager.Domain.Postings.AggregatePeriod.Month && a.PeriodStart == startMonth).Amount.Should().Be(-50m);
        conn.Dispose();
    }
}
