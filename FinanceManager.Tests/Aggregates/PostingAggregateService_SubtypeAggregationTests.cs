using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Aggregates;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

        // Assert: expect aggregates for each DateKind and subtype (Booking + Valuta) -> 2 subtypes * 2 date kinds = 4

        var monthStart = new DateTime(2025, 8, 1);
        var quarterStart = new DateTime(2025, 7, 1);
        var halfStart = new DateTime(2025, 7, 1);
        var yearStart = new DateTime(2025, 1, 1);

        int Count(DateTime start, AggregatePeriod p)
            => db.PostingAggregates.Count(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == p && a.PeriodStart == start);

        // Expect 4 aggregates now (Booking+Valuta × Dividend+Tax)
        Assert.Equal(4, Count(monthStart, AggregatePeriod.Month));
        Assert.Equal(4, Count(quarterStart, AggregatePeriod.Quarter));
        Assert.Equal(4, Count(halfStart, AggregatePeriod.HalfYear));
        Assert.Equal(4, Count(yearStart, AggregatePeriod.Year));

        // Also ensure amounts are present: each subtype appears for both DateKinds
        var amountsMonth = db.PostingAggregates
            .Where(a => a.Kind == PostingKind.Security && a.SecurityId == securityId && a.Period == AggregatePeriod.Month && a.PeriodStart == monthStart)
            .Select(a => a.Amount)
            .AsEnumerable()
            .OrderBy(x => x)
            .ToArray();

        // Expect two occurrences of each amount (one per DateKind)
        Assert.Equal(2, amountsMonth.Count(x => x == 1.64m));
        Assert.Equal(2, amountsMonth.Count(x => x == -0.24m));
    }
}
