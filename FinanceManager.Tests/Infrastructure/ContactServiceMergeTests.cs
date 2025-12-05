using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Contacts;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.Infrastructure;

public sealed class ContactServiceMergeTests
{
    private static AppDbContext CreateSqliteContext()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(conn)
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task Merge_ShouldReassign_Postings_And_StatementEntries_ToTarget()
    {
        using var db = CreateSqliteContext();
        var owner = Guid.NewGuid();
        var src = new FinanceManager.Domain.Contacts.Contact(owner, "Source", ContactType.Person, null, null, false);
        var tgt = new FinanceManager.Domain.Contacts.Contact(owner, "Target", ContactType.Person, null, null, false);
        db.Contacts.AddRange(src, tgt);
        await db.SaveChangesAsync();

        // Seed a posting that references the source contact
        var posting = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Contact,
            accountId: null, contactId: src.Id, savingsPlanId: null, securityId: null,
            bookingDate: DateTime.UtcNow.Date, amount: 10m, subject: "S", recipientName: null, description: null, securitySubType: null);
        db.Postings.Add(posting);

        // Seed a statement entry that references the source contact (via EF entry since private setter)
        var se = new FinanceManager.Domain.Statements.StatementEntry(Guid.NewGuid(), DateTime.UtcNow.Date, 10m, "Subj", Guid.NewGuid().ToString(), null, null, "EUR", null, false, false);
        db.StatementEntries.Add(se);
        await db.SaveChangesAsync();
        db.Entry(se).Property<Guid?>("ContactId").CurrentValue = src.Id;
        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        await svc.MergeAsync(owner, src.Id, tgt.Id, CancellationToken.None);

        // Posting now references target
        var reloadedPosting = await db.Postings.AsNoTracking().FirstAsync(p => p.Id == posting.Id);
        Assert.Equal(tgt.Id, reloadedPosting.ContactId);

        // StatementEntry now references target
        var reloadedSe = await db.StatementEntries.AsNoTracking().FirstAsync(x => x.Id == se.Id);
        Assert.Equal(tgt.Id, reloadedSe.ContactId);

        // Source removed
        Assert.Null(await db.Contacts.FindAsync(src.Id));
    }

    [Fact]
    public async Task Merge_ShouldMergeAndReassign_PostingAggregates_WithoutDuplicates()
    {
        using var db = CreateSqliteContext();
        var owner = Guid.NewGuid();
        var src = new FinanceManager.Domain.Contacts.Contact(owner, "Source", ContactType.Person, null, null, false);
        var tgt = new FinanceManager.Domain.Contacts.Contact(owner, "Target", ContactType.Person, null, null, false);
        db.Contacts.AddRange(src, tgt);
        await db.SaveChangesAsync();

        var keyPeriodStart = new DateTime(2024, 1, 1);
        var period = FinanceManager.Domain.Postings.AggregatePeriod.Month;

        // Existing target aggregate for Contact kind at period
        var targetAgg = new FinanceManager.Domain.Postings.PostingAggregate(
            PostingKind.Contact,
            accountId: null,
            contactId: tgt.Id,
            savingsPlanId: null,
            securityId: null,
            periodStart: keyPeriodStart,
            period: period);
        // bump to 100
        targetAgg.Add(100m);
        db.PostingAggregates.Add(targetAgg);

        // Source aggregate with same key (should merge into targetAgg)
        var srcAggSameKey = new FinanceManager.Domain.Postings.PostingAggregate(
            PostingKind.Contact,
            accountId: null,
            contactId: src.Id,
            savingsPlanId: null,
            securityId: null,
            periodStart: keyPeriodStart,
            period: period);
        srcAggSameKey.Add(50m);
        db.PostingAggregates.Add(srcAggSameKey);

        // Another source aggregate with different period (should be reassigned without merge)
        var srcAggOther = new FinanceManager.Domain.Postings.PostingAggregate(
            PostingKind.Contact,
            accountId: null,
            contactId: src.Id,
            savingsPlanId: null,
            securityId: null,
            periodStart: new DateTime(2024, 2, 1),
            period: period);
        srcAggOther.Add(30m);
        db.PostingAggregates.Add(srcAggOther);

        await db.SaveChangesAsync();

        var svc = new ContactService(db);
        await svc.MergeAsync(owner, src.Id, tgt.Id, CancellationToken.None);

        var aggs = await db.PostingAggregates.AsNoTracking().Where(a => a.Kind == PostingKind.Contact).ToListAsync();
        // Source aggregate with same key should be removed, target amount summed (100 + 50)
        var merged = aggs.Single(a => a.ContactId == tgt.Id && a.PeriodStart == keyPeriodStart && a.Period == period);
        Assert.Equal(150m, merged.Amount);
        // The other source agg should be reassigned to target
        Assert.DoesNotContain(aggs, a => a.ContactId == src.Id);
        Assert.Contains(aggs, a => a.ContactId == tgt.Id && a.PeriodStart == new DateTime(2024, 2, 1));
    }
}
