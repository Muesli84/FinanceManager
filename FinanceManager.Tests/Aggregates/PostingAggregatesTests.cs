using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Statements;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

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

    [Fact]
    public async Task UpsertAggregates_ShouldNotCreateDuplicates_ForSameKey_InSingleContextSession()
    {
        using var db = CreateSqliteContext();
        var svc = new StatementDraftService(db);

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
        var dups = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth)
            .CountAsync();
        Assert.Equal(1, dups);
    }

    [Fact]
    public async Task UpsertAggregates_ShouldHonorUniqueIndex_AcrossSaves()
    {
        using var db = CreateSqliteContext();
        var svc = new StatementDraftService(db);

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
        var count = await db.PostingAggregates
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == AggregatePeriod.Month && x.PeriodStart == keyMonth)
            .CountAsync();
        Assert.Equal(1, count);
    }
}
