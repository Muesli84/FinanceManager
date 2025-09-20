using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain; // for AccountType, PostingKind, StatementDraftStatus
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ContactType = FinanceManager.Shared.Dtos.ContactType; // shared enum

namespace FinanceManager.Tests.Statements;

/// <summary>
/// Tests for booking split drafts that are grouped only by a shared UploadGroupId (multi-part upload) instead of a single explicit SplitDraftId link.
/// Expected behaviour:
///  - Parent draft has an intermediary entry (Contact.IsPaymentIntermediary = true).
///  - User links ONE of the child drafts (same upload group) via SetEntrySplitDraft.
///  - Validation and booking must treat ALL drafts of that upload group (without account + still Draft) as children.
///  - Sum check compares parent entry amount with sum of ALL child draft entry amounts across the group.
///  - Booking parent books all grouped child drafts (and commits them) while unrelated drafts remain untouched.
/// </summary>
public sealed class StatementDraftUploadGroupSplitTests
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
        var owner = new FinanceManager.Domain.Users.User("owner", "hash", true);
        db.Users.Add(owner);
        db.SaveChanges();
        // self + bank contact base (needed in some validations later maybe)
        var self = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(self);
        db.SaveChanges();
        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, conn, owner.Id);
    }

    private static async Task<(Account account, Contact bank)> AddAccountAsync(AppDbContext db, Guid owner)
    {
        var bank = new Contact(owner, "Bank", ContactType.Bank, null, null);
        db.Contacts.Add(bank);
        await db.SaveChangesAsync();
        var acc = new Account(owner, AccountType.Giro, "Konto", "DE00", bank.Id);
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();
        return (acc, bank);
    }

    private static async Task<StatementDraft> CreateDraftAsync(AppDbContext db, Guid owner, Guid? accountId = null, Guid? uploadGroupId = null)
    {
        var d = new StatementDraft(owner, "file.pdf", null, null);
        if (uploadGroupId != null)
        {
            d.SetUploadGroup(uploadGroupId.Value);
        }
        if (accountId != null)
        {
            d.SetDetectedAccount(accountId.Value);
        }
        db.StatementDrafts.Add(d);
        await db.SaveChangesAsync();
        return d;
    }

    [Fact]
    public async Task Booking_GroupedSplitDrafts_AllChildrenBooked_IndependentsRemain()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);

        // Parent draft with intermediary root entry amount 300
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, isPaymentIntermediary: true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 300m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false);
        db.Entry(pEntry).State = EntityState.Added;
        pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        // Upload group with three child drafts: 120 + 80 + 100 = 300
        var groupId = Guid.NewGuid();
        var child1 = await CreateDraftAsync(db, owner, null, groupId);
        var child2 = await CreateDraftAsync(db, owner, null, groupId);
        var child3 = await CreateDraftAsync(db, owner, null, groupId);

        var cA = new Contact(owner, "Alice", ContactType.Person, null, null);
        var cB = new Contact(owner, "Bob", ContactType.Person, null, null);
        var cC = new Contact(owner, "Carol", ContactType.Person, null, null);
        db.Contacts.AddRange(cA, cB, cC);
        await db.SaveChangesAsync();

        var e1 = child1.AddEntry(DateTime.Today, 120m, "A", cA.Name, DateTime.Today, "EUR", null, false); e1.MarkAccounted(cA.Id); db.Entry(e1).State = EntityState.Added;
        var e2 = child2.AddEntry(DateTime.Today, 80m, "B", cB.Name, DateTime.Today, "EUR", null, false); e2.MarkAccounted(cB.Id); db.Entry(e2).State = EntityState.Added;
        var e3 = child3.AddEntry(DateTime.Today, 100m, "C", cC.Name, DateTime.Today, "EUR", null, false); e3.MarkAccounted(cC.Id); db.Entry(e3).State = EntityState.Added;
        await db.SaveChangesAsync();

        // Unrelated independent draft (should survive)
        var independent = await CreateDraftAsync(db, owner, null, Guid.NewGuid());
        var indEntry = independent.AddEntry(DateTime.Today, 50m, "Ind", cA.Name, DateTime.Today, "EUR", null, false); db.Entry(indEntry).State = EntityState.Added; indEntry.MarkAccounted(cA.Id);
        await db.SaveChangesAsync();

        // Link only one of the child drafts (implementation should pick up all same upload group drafts)
        pEntry.AssignSplitDraft(child2.Id); // arbitrary representative
        await db.SaveChangesAsync();

        // Act
        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);

        // Assert (expected success once implementation aggregates all three children)
        res.Success.Should().BeTrue("sum of grouped child drafts (300) equals parent entry amount 300");

        // All three grouped drafts committed
        (await db.StatementDrafts.FindAsync(child1.Id))!.Status.Should().Be(StatementDraftStatus.Committed);
        (await db.StatementDrafts.FindAsync(child2.Id))!.Status.Should().Be(StatementDraftStatus.Committed);
        (await db.StatementDrafts.FindAsync(child3.Id))!.Status.Should().Be(StatementDraftStatus.Committed);
        (await db.StatementDrafts.FindAsync(parent.Id))!.Status.Should().Be(StatementDraftStatus.Committed);

        // Independent draft remains Draft
        (await db.StatementDrafts.FindAsync(independent.Id))!.Status.Should().Be(StatementDraftStatus.Draft);

        // Postings: parent zero postings + 3 children with normal postings (each 2) => 1 parent (0 bank + 0 contact) + 3*2 child = 8 total postings of kind Bank/Contact
        var bank = db.Postings.Where(p => p.Kind == PostingKind.Bank).ToList();
        var contact = db.Postings.Where(p => p.Kind == PostingKind.Contact).ToList();
        bank.Count.Should().Be(1 + 3); // 1 zero + 3 real
        contact.Count.Should().Be(1 + 3);
        bank.Single(p => p.Amount == 0m).Should().NotBeNull();
        contact.Single(p => p.Amount == 0m).Should().NotBeNull();
        bank.Where(p => p.Amount != 0m).Sum(p => p.Amount).Should().Be(300m);
        contact.Where(p => p.Amount != 0m).Sum(p => p.Amount).Should().Be(300m);

        conn.Dispose();
    }

    [Fact]
    public async Task Booking_GroupedSplitDrafts_Fails_WhenSumLessThanParent()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary);
        await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 300m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false); db.Entry(pEntry).State = EntityState.Added; pEntry.MarkAccounted(intermediary.Id);
        await db.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        var child1 = await CreateDraftAsync(db, owner, null, groupId);
        var child2 = await CreateDraftAsync(db, owner, null, groupId);
        var cA = new Contact(owner, "Alice", ContactType.Person, null, null);
        var cB = new Contact(owner, "Bob", ContactType.Person, null, null);
        db.Contacts.AddRange(cA, cB); await db.SaveChangesAsync();
        var e1 = child1.AddEntry(DateTime.Today, 100m, "A", cA.Name, DateTime.Today, "EUR", null, false); e1.MarkAccounted(cA.Id); db.Entry(e1).State = EntityState.Added;
        var e2 = child2.AddEntry(DateTime.Today, 150m, "B", cB.Name, DateTime.Today, "EUR", null, false); e2.MarkAccounted(cB.Id); db.Entry(e2).State = EntityState.Added;
        await db.SaveChangesAsync(); // sum = 250 < 300

        pEntry.AssignSplitDraft(child1.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SPLIT_AMOUNT_MISMATCH").Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task Booking_GroupedSplitDrafts_Fails_WhenSumGreaterThanParent()
    {
        var (sut, db, conn, owner) = Create();
        var (acc, _) = await AddAccountAsync(db, owner);
        var parent = await CreateDraftAsync(db, owner, acc.Id);
        var intermediary = new Contact(owner, "PayService", ContactType.Organization, null, null, true);
        db.Contacts.Add(intermediary); await db.SaveChangesAsync();
        var pEntry = parent.AddEntry(DateTime.Today, 300m, "Root", intermediary.Name, DateTime.Today, "EUR", null, false); db.Entry(pEntry).State = EntityState.Added; pEntry.MarkAccounted(intermediary.Id); await db.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        var child1 = await CreateDraftAsync(db, owner, null, groupId);
        var child2 = await CreateDraftAsync(db, owner, null, groupId);
        var child3 = await CreateDraftAsync(db, owner, null, groupId);
        var cA = new Contact(owner, "Alice", ContactType.Person, null, null);
        var cB = new Contact(owner, "Bob", ContactType.Person, null, null);
        var cC = new Contact(owner, "Carol", ContactType.Person, null, null);
        db.Contacts.AddRange(cA, cB, cC); await db.SaveChangesAsync();
        var e1 = child1.AddEntry(DateTime.Today, 120m, "A", cA.Name, DateTime.Today, "EUR", null, false); e1.MarkAccounted(cA.Id); db.Entry(e1).State = EntityState.Added;
        var e2 = child2.AddEntry(DateTime.Today, 110m, "B", cB.Name, DateTime.Today, "EUR", null, false); e2.MarkAccounted(cB.Id); db.Entry(e2).State = EntityState.Added;
        var e3 = child3.AddEntry(DateTime.Today, 130m, "C", cC.Name, DateTime.Today, "EUR", null, false); e3.MarkAccounted(cC.Id); db.Entry(e3).State = EntityState.Added; // total 360 > 300
        await db.SaveChangesAsync();

        pEntry.AssignSplitDraft(child2.Id);
        await db.SaveChangesAsync();

        var res = await sut.BookAsync(parent.Id, null, owner, false, CancellationToken.None);
        res.Success.Should().BeFalse();
        res.Validation.Messages.Any(m => m.Code == "SPLIT_AMOUNT_MISMATCH").Should().BeTrue();
        conn.Dispose();
    }
}
