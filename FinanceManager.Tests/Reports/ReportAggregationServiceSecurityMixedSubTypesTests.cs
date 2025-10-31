using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Shared.Dtos; // SecurityPostingSubType
using FinanceManager.Domain.Reports; // ReportInterval
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class ReportAggregationServiceSecurityMixedSubTypesTests
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
    public async Task QueryAsync_SecurityMonth_NoSubtypeFilter_ShouldSumAllSubTypes()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var sec = new FinanceManager.Domain.Securities.Security(user.Id, "ACME CORP", "ACME-ISIN", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync();

        // Target month (analysis)
        var analysis = new DateTime(2025, 8, 1);

        // Seed four separate aggregates for the same month+security but different sub types
        void Add(SecurityPostingSubType subtype, decimal amount)
        {
            var agg = new PostingAggregate(PostingKind.Security, null, null, null, sec.Id, analysis, AggregatePeriod.Month, subtype);
            agg.Add(amount);
            db.PostingAggregates.Add(agg);
        }

        // Example amounts (Buy negative, Fee negative, Dividend positive, Tax negative)
        Add(SecurityPostingSubType.Buy, -1000.00m);
        Add(SecurityPostingSubType.Fee, -4.50m);
        Add(SecurityPostingSubType.Dividend, 25.75m);
        Add(SecurityPostingSubType.Tax, -3.90m);
        await db.SaveChangesAsync();

        var sut = new ReportAggregationService(db);
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Month,
            Take: 12,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: null // no subtype filter -> include all
        );

        var result = await sut.QueryAsync(query, CancellationToken.None);
        Assert.Equal(ReportInterval.Month, result.Interval);
        Assert.NotEmpty(result.Points);

        var key = $"Security:{sec.Id}";
        var row = result.Points.Single(p => p.GroupKey == key && p.PeriodStart == analysis);

        var expected = -1000.00m - 4.50m + 25.75m - 3.90m; // sum of all sub types
        Assert.Equal(expected, row.Amount);

        // Ensure only one row for that security+month (aggregated across sub types)
        Assert.Equal(1, result.Points.Count(p => p.GroupKey == key && p.PeriodStart == analysis));
    }
}
