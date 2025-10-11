using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Reports;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Shared.Dtos; // SecurityPostingSubType

namespace FinanceManager.Tests.Reports;

public sealed class SecurityDividendNetYtdSimpleTests
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
    public async Task Query_Ytd_WithIncludeDividendRelated_ShouldSumDividendFeeTax_ForCurrentYear()
    {
        using var db = CreateDb();
        var ct = CancellationToken.None;

        // Arrange: owner and one owned security
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var sec = new FinanceManager.Domain.Securities.Security(user.Id, "ETF Test", "DE000TEST", null, null, "EUR", null);
        db.Securities.Add(sec);
        await db.SaveChangesAsync(ct);

        // Create three postings on Jan 1st of current year for the same security and same (default) group
        var year = DateTime.UtcNow.Year;
        var date = new DateTime(year, 1, 1);

        // Dividend +10, Tax +1, Fee +0.5 -> expected net sum 11.5 for that month
        var pDiv = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: sec.Id,
            bookingDate: date,
            amount: 10.0m,
            subject: "Dividend",
            recipientName: null,
            description: null,
            securitySubType: SecurityPostingSubType.Dividend,
            quantity: null);

        var pTax = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: sec.Id,
            bookingDate: date,
            amount: 1.0m,
            subject: "Tax",
            recipientName: null,
            description: null,
            securitySubType: SecurityPostingSubType.Tax,
            quantity: null);

        var pFee = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: sec.Id,
            bookingDate: date,
            amount: 0.5m,
            subject: "Fee",
            recipientName: null,
            description: null,
            securitySubType: SecurityPostingSubType.Fee,
            quantity: null);

        // Dividend +10, Tax +1, Fee +0.5 -> expected net sum 11.5 for that month
        var pDiv2 = new FinanceManager.Domain.Postings.Posting(
            sourceId: Guid.NewGuid(),
            kind: PostingKind.Security,
            accountId: null,
            contactId: null,
            savingsPlanId: null,
            securityId: sec.Id,
            bookingDate: new DateTime(date.Year, 12, 31),
            amount: 10.0m,
            subject: "Dividend",
            recipientName: null,
            description: null,
            securitySubType: SecurityPostingSubType.Dividend,
            quantity: null);

        db.Postings.AddRange(pDiv, pTax, pFee, pDiv2);
        await db.SaveChangesAsync(ct);

        var sut = new ReportAggregationService(db);

        // Act: YTD report for Security with IncludeDividendRelated and Dividend subtype selected
        var analysis = new DateTime(year, 6, 1);
        var query = new ReportAggregationQuery(
            OwnerUserId: user.Id,
            PostingKind: (int)PostingKind.Security,
            Interval: ReportInterval.Ytd,
            Take: 12,
            IncludeCategory: false,
            ComparePrevious: false,
            CompareYear: false,
            PostingKinds: null,
            AnalysisDate: analysis,
            Filters: new ReportAggregationFilters(
                SecurityIds: new[] { sec.Id },
                SecuritySubTypes: new[] { (int)SecurityPostingSubType.Dividend },
                IncludeDividendRelated: true)
        );

        var result = await sut.QueryAsync(query, ct);

        // Assert: one point for the security, YTD anchored to Jan 1st of current year, sum = 11.5
        result.Interval.Should().Be(ReportInterval.Ytd);
        var periodStart = new DateTime(year, 1, 1);
        var row = result.Points.Single(p => p.GroupKey == $"Security:{sec.Id}" && p.PeriodStart == periodStart);
        row.Amount.Should().Be(11.5m);
    }
}
