using FinanceManager.Application.Aggregates;
using FinanceManager.Domain.Accounts; // for Account, AccountType
using FinanceManager.Domain.Contacts; // for Contact
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Infrastructure.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Tests.Aggregates;

public sealed class PostingAggregatesTests
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

    private static StatementDraftService CreateService(AppDbContext db)
    {
        IPostingAggregateService agg = new PostingAggregateService(db);
        return new StatementDraftService(db, agg);
    }

    [Fact]
    public async Task UpsertAggregates_ShouldNotCreateDuplicates_ForSameKey_InSingleContextSession()
    {
        using var db = CreateSqliteContext();
        var svc = CreateService(db);

        var accountId = Guid.NewGuid();
        var bookingDate = new DateTime(2017, 1, 15);
        var p1 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, bookingDate, 100m, null, null, null, null);
        var p2 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, bookingDate, 50m, null, null, null, null);

        var method = typeof(StatementDraftService).GetMethod("UpsertAggregatesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var ct = CancellationToken.None;
        await (Task)method!.Invoke(svc, new object[] { p1, ct })!;
        await (Task)method!.Invoke(svc, new object[] { p2, ct })!;
        await db.SaveChangesAsync();

        var keyMonth = new DateTime(2017, 1, 1);
        // Expect two aggregates for the same period: one for Booking and one for Valuta
        var dups = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth)
            .CountAsync();
        Assert.Equal(2, dups);

        // Verify each DateKind has the summed amount (150)
        var bookingAgg = await db.PostingAggregates.FirstOrDefaultAsync(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth && x.DateKind == AggregateDateKind.Booking);
        var valutaAgg = await db.PostingAggregates.FirstOrDefaultAsync(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth && x.DateKind == AggregateDateKind.Valuta);
        Assert.NotNull(bookingAgg);
        Assert.NotNull(valutaAgg);
        Assert.Equal(150m, bookingAgg!.Amount);
        Assert.Equal(150m, valutaAgg!.Amount);
    }

    [Fact]
    public async Task UpsertAggregates_ShouldHonorUniqueIndex_AcrossSaves()
    {
        using var db = CreateSqliteContext();
        var svc = CreateService(db);

        var accountId = Guid.NewGuid();
        var bookingDate = new DateTime(2017, 1, 10);
        var p1 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, bookingDate, 100m, null, null, null, null);
        var p2 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, bookingDate.AddDays(5), 50m, null, null, null, null);

        var method = typeof(StatementDraftService).GetMethod("UpsertAggregatesAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var ct = CancellationToken.None;
        await (Task)method!.Invoke(svc, new object[] { p1, ct })!;
        await db.SaveChangesAsync();
        await (Task)method!.Invoke(svc, new object[] { p2, ct })!;
        await db.SaveChangesAsync();

        var keyMonth = new DateTime(2017, 1, 1);
        // Expect two aggregates (Booking + Valuta) for the account/month
        var count = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth)
            .CountAsync();
        Assert.Equal(2, count);

        // Verify amounts per DateKind
        var bookingSum = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth && x.DateKind == AggregateDateKind.Booking)
            .Select(x => x.Amount).SingleAsync();
        var valutaSum = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth && x.DateKind == AggregateDateKind.Valuta)
            .Select(x => x.Amount).SingleAsync();
        Assert.Equal(150m, bookingSum);
        Assert.Equal(150m, valutaSum);
    }

    [Fact]
    public async Task Rebuild_ShouldCreateBookingAndValutaAggregates_AndSeparateValutaPeriods()
    {
        using var db = CreateSqliteContext();
        var svc = new PostingAggregateService(db);
        var ct = CancellationToken.None;

        var userId = Guid.NewGuid();
        // create contact first (bank contact) and then account that references it
        var contact = new Contact(userId, "C", ContactType.Bank, null);
        db.Contacts.Add(contact);
        await db.SaveChangesAsync();

        var acc = new Account(userId, AccountType.Giro, "A1", null, contact.Id);
        db.Accounts.Add(acc);
        await db.SaveChangesAsync();

        var accountId = acc.Id;

        var year = 2020;
        // p1: booking Jan 10, valuta Jan 31 -> both in Jan
        var p1 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, new DateTime(year, 1, 10), new DateTime(year, 1, 31), 100m, null, null, null, null, null);
        // p2: booking Jan 11, valuta Feb 1 -> booking Jan, valuta Feb
        var p2 = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.Bank, accountId, null, null, null, new DateTime(year, 1, 11), new DateTime(year, 2, 1), 200m, null, null, null, null, null);

        // add postings to DB and run rebuild
        db.Postings.AddRange(p1, p2);
        await db.SaveChangesAsync();

        // run rebuild for the user
        await svc.RebuildForUserAsync(userId, (done, total) => { }, ct);

        // Booking aggregates: Jan should sum both = 300
        var janStart = new DateTime(year, 1, 1);
        var bookingJan = await db.PostingAggregates.Where(a => a.Kind == PostingKind.Bank && a.AccountId == accountId && a.Period == AggregatePeriod.Month && a.PeriodStart == janStart && a.DateKind == AggregateDateKind.Booking).SingleAsync();
        Assert.Equal(300m, bookingJan.Amount);

        // Valuta aggregates: Jan = 100, Feb = 200
        var valutaJan = await db.PostingAggregates.Where(a => a.Kind == PostingKind.Bank && a.AccountId == accountId && a.Period == AggregatePeriod.Month && a.PeriodStart == janStart && a.DateKind == AggregateDateKind.Valuta).SingleAsync();
        Assert.Equal(100m, valutaJan.Amount);
        var febStart = new DateTime(year, 2, 1);
        var valutaFeb = await db.PostingAggregates.Where(a => a.Kind == PostingKind.Bank && a.AccountId == accountId && a.Period == AggregatePeriod.Month && a.PeriodStart == febStart && a.DateKind == AggregateDateKind.Valuta).SingleAsync();
        Assert.Equal(200m, valutaFeb.Amount);
    }
}
