using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FinanceManager.Tests.Statements;

public sealed class StatementDraftLinkingTests
{
    private static (StatementDraftService sut, AppDbContext db, Guid ownerId) Create()
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
        var ownerContact = new Contact(owner.Id, "Ich", ContactType.Self, null, null);
        db.Contacts.Add(ownerContact);
        db.SaveChanges();

        var sut = new StatementDraftService(db, new PostingAggregateService(db));
        return (sut, db, owner.Id);
    }

    [Fact]
    public async Task Book_TwoMatchingSelfTransfers_ShouldLinkContactPostings()
    {
        var (sut, db, owner) = Create();

        // accounts
        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        // drafts
        var draftA = new StatementDraft(owner, "a.csv", "", null);
        var entryA = draftA.AddEntry(DateTime.UtcNow.Date, -100m, "Transfer to savings");
        draftA.SetDetectedAccount(giro.Id);
        db.StatementDrafts.Add(draftA);

        var draftB = new StatementDraft(owner, "b.csv", "", null);
        var entryB = draftB.AddEntry(DateTime.UtcNow.Date, 100m, "Transfer to savings");
        draftB.SetDetectedAccount(savings.Id);
        db.StatementDrafts.Add(draftB);

        // mark entries as self contact so booking creates contact postings
        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;
        entryA.MarkAccounted(selfContactId);
        entryB.MarkAccounted(selfContactId);

        await db.SaveChangesAsync();

        // Book draft A then B
        var resA = await sut.BookAsync(draftA.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resA.Success);

        var resB = await sut.BookAsync(draftB.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resB.Success);

        // Find contact postings created for entries
        var contactP_A = db.Postings.FirstOrDefault(p => p.SourceId == entryA.Id && p.Kind == PostingKind.Contact);
        var contactP_B = db.Postings.FirstOrDefault(p => p.SourceId == entryB.Id && p.Kind == PostingKind.Contact);

        Assert.NotNull(contactP_A);
        Assert.NotNull(contactP_B);
        Assert.NotNull(contactP_A.LinkedPostingId);
        Assert.NotNull(contactP_B.LinkedPostingId);
        Assert.Equal(contactP_A.LinkedPostingId, contactP_B.Id);
        Assert.Equal(contactP_B.LinkedPostingId, contactP_A.Id);
    }

    [Fact]
    public async Task Book_AmountsMismatch_ShouldNotLink()
    {
        var (sut, db, owner) = Create();

        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        var draftA = new StatementDraft(owner, "a.csv", "", null);
        var entryA = draftA.AddEntry(DateTime.UtcNow.Date, -90m, "Transfer");
        draftA.SetDetectedAccount(giro.Id);
        db.StatementDrafts.Add(draftA);

        var draftB = new StatementDraft(owner, "b.csv", "", null);
        var entryB = draftB.AddEntry(DateTime.UtcNow.Date, 100m, "Transfer");
        draftB.SetDetectedAccount(savings.Id);
        db.StatementDrafts.Add(draftB);

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;
        entryA.MarkAccounted(selfContactId);
        entryB.MarkAccounted(selfContactId);

        await db.SaveChangesAsync();

        var resA = await sut.BookAsync(draftA.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resA.Success);
        var resB = await sut.BookAsync(draftB.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resB.Success);

        var contactP_A = db.Postings.FirstOrDefault(p => p.SourceId == entryA.Id && p.Kind == PostingKind.Contact);
        var contactP_B = db.Postings.FirstOrDefault(p => p.SourceId == entryB.Id && p.Kind == PostingKind.Contact);

        Assert.NotNull(contactP_A);
        Assert.NotNull(contactP_B);
        Assert.Null(contactP_A.LinkedPostingId);
        Assert.Null(contactP_B.LinkedPostingId);
    }

    [Fact]
    public async Task Book_SameAccount_ShouldNotLink()
    {
        var (sut, db, owner) = Create();

        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        db.Accounts.Add(giro);
        await db.SaveChangesAsync();

        var draftA = new StatementDraft(owner, "a.csv", "", null);
        var entryA = draftA.AddEntry(DateTime.UtcNow.Date, -100m, "Xfer");
        draftA.SetDetectedAccount(giro.Id);
        db.StatementDrafts.Add(draftA);

        var draftB = new StatementDraft(owner, "b.csv", "", null);
        var entryB = draftB.AddEntry(DateTime.UtcNow.Date, 100m, "Xfer");
        draftB.SetDetectedAccount(giro.Id);
        db.StatementDrafts.Add(draftB);

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;
        entryA.MarkAccounted(selfContactId);
        entryB.MarkAccounted(selfContactId);

        await db.SaveChangesAsync();

        var resA = await sut.BookAsync(draftA.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resA.Success);
        var resB = await sut.BookAsync(draftB.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resB.Success);

        var contactP_A = db.Postings.FirstOrDefault(p => p.SourceId == entryA.Id && p.Kind == PostingKind.Contact);
        var contactP_B = db.Postings.FirstOrDefault(p => p.SourceId == entryB.Id && p.Kind == PostingKind.Contact);

        Assert.NotNull(contactP_A);
        Assert.NotNull(contactP_B);
        Assert.Null(contactP_A.LinkedPostingId);
        Assert.Null(contactP_B.LinkedPostingId);
    }

    [Fact]
    public async Task Book_WithSavingsPlan_ShouldLink()
    {
        var (sut, db, owner) = Create();

        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        // create a savings plan
        var plan = new FinanceManager.Domain.Savings.SavingsPlan(owner, "Plan", SavingsPlanType.Recurring, 1000m, DateTime.UtcNow.AddMonths(1), SavingsPlanInterval.Monthly);
        db.SavingsPlans.Add(plan);

        var draftA = new StatementDraft(owner, "a.csv", "", null);
        var entryA = draftA.AddEntry(DateTime.UtcNow.Date, -100m, "SP");
        draftA.SetDetectedAccount(giro.Id);
        db.StatementDrafts.Add(draftA);

        var draftB = new StatementDraft(owner, "b.csv", "", null);
        var eB = draftB.AddEntry(DateTime.UtcNow.Date, 100m, "SP");
        draftB.SetDetectedAccount(savings.Id);
        db.StatementDrafts.Add(draftB);

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;
        // assign savings plan to entry A (giro side) — savings-account entries must not carry a savings-plan
        entryA.AssignSavingsPlan(plan.Id);
        entryA.MarkAccounted(selfContactId);
        eB.MarkAccounted(selfContactId);

        await db.SaveChangesAsync();

        var resA = await sut.BookAsync(draftA.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resA.Success);
        var resB = await sut.BookAsync(draftB.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resB.Success);

        var contactP_A = db.Postings.FirstOrDefault(p => p.SourceId == entryA.Id && p.Kind == PostingKind.Contact);
        var contactP_B = db.Postings.FirstOrDefault(p => p.SourceId == eB.Id && p.Kind == PostingKind.Contact);

        Assert.NotNull(contactP_A);
        Assert.NotNull(contactP_B);
        Assert.NotNull(contactP_A.LinkedPostingId);
        Assert.NotNull(contactP_B.LinkedPostingId);
        Assert.Equal(contactP_A.LinkedPostingId, contactP_B.Id);
        Assert.Equal(contactP_B.LinkedPostingId, contactP_A.Id);
    }

    [Fact]
    public async Task Book_TwelveMonthlyMatchingSelfTransfers_MonthlyBothSides_ShouldLinkAll()
    {
        var (sut, db, owner) = Create();

        // accounts
        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;

        var amount = 100m;
        var start = DateTime.UtcNow.Date.AddMonths(-11);

        // Simulate monthly import: each month a Giro and Savings statement imported and booked
        for (int i = 0; i < 12; i++)
        {
            var date = start.AddMonths(i);

            var draftG = new StatementDraft(owner, $"giro_{i}.csv", "", null);
            var eG = draftG.AddEntry(date, -amount, $"Monthly transfer {i}");
            draftG.SetDetectedAccount(giro.Id);
            eG.MarkAccounted(selfContactId);
            db.StatementDrafts.Add(draftG);

            var draftS = new StatementDraft(owner, $"sav_{i}.csv", "", null);
            var eS = draftS.AddEntry(date, amount, $"Monthly transfer {i}");
            draftS.SetDetectedAccount(savings.Id);
            eS.MarkAccounted(selfContactId);
            db.StatementDrafts.Add(draftS);

            await db.SaveChangesAsync();

            var resG = await sut.BookAsync(draftG.Id, null, owner, forceWarnings: true, CancellationToken.None);
            Assert.True(resG.Success);
            var resS = await sut.BookAsync(draftS.Id, null, owner, forceWarnings: true, CancellationToken.None);
            Assert.True(resS.Success);

            // verify that the pair for this month got linked
            var contactP_G = db.Postings.FirstOrDefault(p => p.SourceId == eG.Id && p.Kind == PostingKind.Contact);
            var contactP_S = db.Postings.FirstOrDefault(p => p.SourceId == eS.Id && p.Kind == PostingKind.Contact);
            Assert.NotNull(contactP_G);
            Assert.NotNull(contactP_S);
            Assert.NotNull(contactP_G.LinkedPostingId);
            Assert.NotNull(contactP_S.LinkedPostingId);
            Assert.Equal(contactP_G.LinkedPostingId, contactP_S.Id);
            Assert.Equal(contactP_S.LinkedPostingId, contactP_G.Id);
        }

        // Final sanity: ensure total number of contact postings is 24 (12 pairs)
        var contactPostings = db.Postings.Where(p => p.Kind == PostingKind.Contact && p.ContactId == selfContactId).ToList();
        Assert.Equal(24, contactPostings.Count);
    }

    [Fact]
    public async Task Book_TwelveGiroThenOneSavingsWithAllTransfers_ShouldLinkAll()
    {
        var (sut, db, owner) = Create();

        // accounts
        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;

        var amount = 100m;
        var start = DateTime.UtcNow.Date.AddMonths(-11);

        // Create and book 12 Giro drafts (one per month)
        var giroEntries = new System.Collections.Generic.List<StatementDraftEntry>();
        for (int i = 0; i < 12; i++)
        {
            var date = start.AddMonths(i);
            var draftG = new StatementDraft(owner, $"giro_{i}.csv", "", null);
            var eG = draftG.AddEntry(date, -amount, $"Monthly transfer {i}");
            draftG.SetDetectedAccount(giro.Id);
            eG.MarkAccounted(selfContactId);
            db.StatementDrafts.Add(draftG);
            await db.SaveChangesAsync();

            var resG = await sut.BookAsync(draftG.Id, null, owner, forceWarnings: true, CancellationToken.None);
            Assert.True(resG.Success);

            giroEntries.Add(eG);
        }

        // Now create a single savings draft that contains all 12 deposits
        var savingsDraft = new StatementDraft(owner, "sav_all.csv", "", null);
        var savingsEntries = new System.Collections.Generic.List<StatementDraftEntry>();
        for (int i = 0; i < 12; i++)
        {
            var date = start.AddMonths(i);
            var eS = savingsDraft.AddEntry(date, amount, $"Monthly transfer {i}");
            eS.MarkAccounted(selfContactId);
            savingsEntries.Add(eS);
        }
        savingsDraft.SetDetectedAccount(savings.Id);
        db.StatementDrafts.Add(savingsDraft);
        await db.SaveChangesAsync();

        var resSAll = await sut.BookAsync(savingsDraft.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resSAll.Success);

        // Verify that each Giro entry has a linked savings posting and vice versa
        for (int i = 0; i < 12; i++)
        {
            var eG = giroEntries[i];
            var eS = savingsEntries[i];

            var contactP_G = db.Postings.FirstOrDefault(p => p.SourceId == eG.Id && p.Kind == PostingKind.Contact);
            var contactP_S = db.Postings.FirstOrDefault(p => p.SourceId == eS.Id && p.Kind == PostingKind.Contact);

            Assert.NotNull(contactP_G);
            Assert.NotNull(contactP_S);
            Assert.NotNull(contactP_G.LinkedPostingId);
            Assert.NotNull(contactP_S.LinkedPostingId);
            Assert.Equal(contactP_G.LinkedPostingId, contactP_S.Id);
            Assert.Equal(contactP_S.LinkedPostingId, contactP_G.Id);
        }

        var contactPostings = db.Postings.Where(p => p.Kind == PostingKind.Contact && p.ContactId == selfContactId).ToList();
        Assert.Equal(24, contactPostings.Count);
    }

    [Fact]
    public async Task Book_TwelveGiro_TwoTransfersPerMonth_ThenOneSavings_AllTransfers_ShouldLinkByPurpose()
    {
        var (sut, db, owner) = Create();

        // accounts
        var giro = new Account(owner, AccountType.Giro, "Giro", null, Guid.NewGuid());
        var savings = new Account(owner, AccountType.Savings, "Spark", null, Guid.NewGuid());
        db.Accounts.AddRange(giro, savings);
        await db.SaveChangesAsync();

        var selfContactId = db.Contacts.First(c => c.OwnerUserId == owner && c.Type == ContactType.Self).Id;

        var amount = 100m;
        var start = DateTime.UtcNow.Date.AddMonths(-11);

        // Create and book 12 Giro drafts, each with two transfers on same day but different subjects
        var giroEntries = new System.Collections.Generic.List<StatementDraftEntry>();
        for (int i = 0; i < 12; i++)
        {
            var date = start.AddMonths(i);
            var draftG = new StatementDraft(owner, $"giro_{i}.csv", "", null);
            var eG1 = draftG.AddEntry(date, -amount, $"Monthly transfer {i} A");
            var eG2 = draftG.AddEntry(date, -amount, $"Monthly transfer {i} B");
            draftG.SetDetectedAccount(giro.Id);
            eG1.MarkAccounted(selfContactId);
            eG2.MarkAccounted(selfContactId);
            db.StatementDrafts.Add(draftG);
            await db.SaveChangesAsync();

            var resG = await sut.BookAsync(draftG.Id, null, owner, forceWarnings: true, CancellationToken.None);
            Assert.True(resG.Success);

            giroEntries.Add(eG1);
            giroEntries.Add(eG2);
        }

        // Now create a single savings draft that contains all deposits (two per month)
        var savingsDraft = new StatementDraft(owner, "sav_all.csv", "", null);
        var savingsEntries = new System.Collections.Generic.List<StatementDraftEntry>();
        for (int i = 0; i < 12; i++)
        {
            var date = start.AddMonths(i);
            var eS1 = savingsDraft.AddEntry(date, amount, $"Monthly transfer {i} A");
            var eS2 = savingsDraft.AddEntry(date, amount, $"Monthly transfer {i} B");
            eS1.MarkAccounted(selfContactId);
            eS2.MarkAccounted(selfContactId);
            savingsEntries.Add(eS1);
            savingsEntries.Add(eS2);
        }
        savingsDraft.SetDetectedAccount(savings.Id);
        db.StatementDrafts.Add(savingsDraft);
        await db.SaveChangesAsync();

        var resSAll = await sut.BookAsync(savingsDraft.Id, null, owner, forceWarnings: true, CancellationToken.None);
        Assert.True(resSAll.Success);

        // Verify that each Giro entry has a linked savings posting with the same subject/purpose
        for (int i = 0; i < giroEntries.Count; i++)
        {
            var eG = giroEntries[i];
            var contactP_G = db.Postings.FirstOrDefault(p => p.SourceId == eG.Id && p.Kind == PostingKind.Contact);
            Assert.NotNull(contactP_G);
            Assert.True(contactP_G.LinkedPostingId.HasValue, "Expected Giro contact posting to be linked to a savings posting");

            var linked = db.Postings.FirstOrDefault(p => p.Id == contactP_G.LinkedPostingId.Value && p.Kind == PostingKind.Contact);
            Assert.NotNull(linked);

            // The matching should consider the usage/purpose (Subject) — currently the service ignores Subject, so this assertion
            // is expected to fail until matching is improved to include Subject in the selection criteria.
            Assert.Equal(contactP_G.Subject, linked.Subject);
        }

        var contactPostings = db.Postings.Where(p => p.Kind == PostingKind.Contact && p.ContactId == selfContactId).ToList();
        Assert.Equal(48, contactPostings.Count);
    }
}
