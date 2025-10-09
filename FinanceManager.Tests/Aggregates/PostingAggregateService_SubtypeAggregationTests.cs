using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using FinanceManager.Shared.Dtos; // SecurityPostingSubType
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Aggregates;

public sealed class PostingAggregateService_SubtypeAggregationTests
{
    private static AppDbContext CreateDb()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task UpsertForPostingAsync_SecurityDividendAndTax_ShouldCreateTwoAggregatesPerInterval()
    {
        using var db = CreateDb();
        var svc = new PostingAggregateService(db);
        var ct = CancellationToken.None;

        var securityId = Guid.NewGuid();
        var date = new DateTime(2025, 8, 19);

        // Arrange two postings for the same security & date with different sub types
        var div = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: securityId,
            bookingDate: date,
            amount: 1.64m,
            subject: null,
            recipientName: null,
            description: "Dividend",
            securitySubType: SecurityPostingSubType.Dividend,
            quantity: null);

        var tax = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: securityId,
            bookingDate: date,
            amount: -0.24m,
            subject: null,
            recipientName: null,
            description: "Tax",
            securitySubType: SecurityPostingSubType.Tax,
            quantity: null);

        // Act
        await svc.UpsertForPostingAsync(div, ct);
        await svc.UpsertForPostingAsync(tax, ct);
        await db.SaveChangesAsync(ct);

        // Assert: expect two aggregates per each period start (sub type separated)

        var monthStart = new DateTime(2025, 8, 1);
        var quarterStart = new DateTime(2025, 7, 1);
        var halfStart = new DateTime(2025, 7, 1);
        var yearStart = new DateTime(2025, 1, 1);

        int Count(DateTime start, AggregatePeriod p)
            => db.PostingAggregates.Count(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == p && a.PeriodStart == start);

        Count(monthStart, AggregatePeriod.Month).Should().Be(2);
        Count(quarterStart, AggregatePeriod.Quarter).Should().Be(2);
        Count(halfStart, AggregatePeriod.HalfYear).Should().Be(2);
        Count(yearStart, AggregatePeriod.Year).Should().Be(2);

        // Also ensure amounts are present (order not guaranteed)
        var amountsMonth = db.PostingAggregates
            .Where(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == AggregatePeriod.Month && a.PeriodStart == monthStart)
            .Select(a => a.Amount)
            .AsEnumerable()
            .OrderBy(x => x)
            .ToArray();
        amountsMonth.Should().Contain(new[] { -0.24m, 1.64m });
    }
}
