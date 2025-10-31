using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Aggregates;

public sealed class PostingAggregateServiceSubtypeSplitTests
{
    private static AppDbContext CreateDb()
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
    public async Task UpsertForPostingAsync_Security_SubTypes_Dividend_And_Tax_ShouldCreateTwoAggregatesPerPeriod()
    {
        using var db = CreateDb();
        var svc = new PostingAggregateService(db);
        var ct = CancellationToken.None;

        var securityId = Guid.NewGuid();
        var date = new DateTime(2025, 8, 19);

        // Create Dividend (+1.64) and Tax (-0.24) postings for same security/date
        var pDiv = new FinanceManager.Domain.Postings.Posting(
            Guid.NewGuid(), PostingKind.Security,
            accountId: null, contactId: null, savingsPlanId: null, securityId: securityId,
            bookingDate: date, amount: 1.64m,
            subject: null, recipientName: null, description: null,
            securitySubType: SecurityPostingSubType.Dividend, quantity: null);

        var pTax = new FinanceManager.Domain.Postings.Posting(
            Guid.NewGuid(), PostingKind.Security,
            accountId: null, contactId: null, savingsPlanId: null, securityId: securityId,
            bookingDate: date, amount: -0.24m,
            subject: null, recipientName: null, description: null,
            securitySubType: SecurityPostingSubType.Tax, quantity: null);

        await svc.UpsertForPostingAsync(pDiv, ct);
        await svc.UpsertForPostingAsync(pTax, ct);
        await db.SaveChangesAsync(ct);

        var monthStart = new DateTime(2025, 8, 1);
        var quarterStart = new DateTime(2025, 7, 1);
        var halfStart = new DateTime(2025, 7, 1);
        var yearStart = new DateTime(2025, 1, 1);

        // Expected: two aggregates per period for the security (one for Dividend, one for Tax) since subtype now part of key
        // Note: aggregates are created per DateKind (Booking + Valuta) resulting in doubled rows -> expect 4
        int CountPer(DateTime start, AggregatePeriod period)
            => db.PostingAggregates.Count(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == period && a.PeriodStart == start);

        CountPer(monthStart, AggregatePeriod.Month).Should().Be(4);
        CountPer(quarterStart, AggregatePeriod.Quarter).Should().Be(4);
        CountPer(halfStart, AggregatePeriod.HalfYear).Should().Be(4);
        CountPer(yearStart, AggregatePeriod.Year).Should().Be(4);

        // And amounts should include both +1.64 and -0.24 for each period
        void AssertAmounts(DateTime start, AggregatePeriod period)
        {
            var amts = db.PostingAggregates
                .Where(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == period && a.PeriodStart == start)
                .Select(a => a.Amount)
                .AsEnumerable() // force client-side ordering to avoid SQLite decimal ORDER BY limitation
                .OrderBy(x => x)
                .ToList();
            amts.Should().Contain(new[] { -0.24m, 1.64m });
        }

        AssertAmounts(monthStart, AggregatePeriod.Month);
        AssertAmounts(quarterStart, AggregatePeriod.Quarter);
        AssertAmounts(halfStart, AggregatePeriod.HalfYear);
        AssertAmounts(yearStart, AggregatePeriod.Year);
    }
}
