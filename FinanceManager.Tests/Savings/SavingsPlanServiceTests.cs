using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Savings;
using FinanceManager.Domain.Savings;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Savings;
using FinanceManager.Shared.Dtos;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain;

public sealed class SavingsPlanServiceTests
{
    private static (SavingsPlanService sut, AppDbContext db, SqliteConnection conn) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var sut = new SavingsPlanService(db);
        return (sut, db, conn);
    }

    [Fact]
    public async Task CreateAndGet_ShouldWork()
    {
        var (sut, db, conn) = Create();
        var owner = Guid.NewGuid();
        var dto = await sut.CreateAsync(owner, "Testplan", SavingsPlanType.OneTime, 1000, DateTime.Today.AddMonths(6), null, null, null, CancellationToken.None);
        var fetched = await sut.GetAsync(dto.Id, owner, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Testplan");
        conn.Dispose();
    }

    [Fact]
    public async Task Update_ShouldChangeValues()
    {
        var (sut, db, conn) = Create();
        var owner = Guid.NewGuid();
        var dto = await sut.CreateAsync(owner, "Testplan", SavingsPlanType.OneTime, 1000, DateTime.Today.AddMonths(6), null, null, null, CancellationToken.None);
        var updated = await sut.UpdateAsync(dto.Id, owner, "Neu", SavingsPlanType.Recurring, 200, null, SavingsPlanInterval.Monthly, null, null, CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Neu");
        updated.Type.Should().Be(SavingsPlanType.Recurring);
        updated.TargetAmount.Should().Be(200);
        updated.Interval.Should().Be(SavingsPlanInterval.Monthly);
        conn.Dispose();
    }

    [Fact]
    public async Task ArchiveAndDelete_ShouldWork()
    {
        var (sut, db, conn) = Create();
        var owner = Guid.NewGuid();
        var dto = await sut.CreateAsync(owner, "Testplan", SavingsPlanType.OneTime, 1000, DateTime.Today.AddMonths(6), null, null, null, CancellationToken.None);
        var ok = await sut.ArchiveAsync(dto.Id, owner, CancellationToken.None);
        ok.Should().BeTrue();
        var deleted = await sut.DeleteAsync(dto.Id, owner, CancellationToken.None);
        deleted.Should().BeTrue();
        conn.Dispose();
    }

    [Fact]
    public async Task AnalyzeAsync_ShortCircuits_When_TargetDateMissing_EvenWithPostings()
    {
        var (sut, db, conn) = Create();
        var owner = Guid.NewGuid();

        // Create an open plan with TargetAmount = 0 and no TargetDate
        var dto = await sut.CreateAsync(owner, "OpenPlan", SavingsPlanType.Open, 0m, null, null, null, null, CancellationToken.None);

        // Add postings for the plan: 100,100,100,-300,50,100 => accumulated 150
        var amounts = new decimal[] { 100m, 100m, 100m, -300m, 50m, 100m };
        foreach (var a in amounts)
        {
            var p = new Posting(
                sourceId: Guid.NewGuid(),
                kind: PostingKind.SavingsPlan,
                accountId: null,
                contactId: null,
                savingsPlanId: dto.Id,
                securityId: null,
                bookingDate: DateTime.UtcNow.Date,
                valutaDate: DateTime.UtcNow.Date,
                amount: a,
                subject: string.Empty,
                recipientName: null,
                description: null,
                securitySubType: null,
                quantity: null);
            db.Postings.Add(p);
        }
        await db.SaveChangesAsync(CancellationToken.None);

        // Act
        var analysis = await sut.AnalyzeAsync(dto.Id, owner, CancellationToken.None);

        // Assert: accumulated amount should reflect actual postings even if TargetDate missing
        analysis.Should().NotBeNull();
        analysis.PlanId.Should().Be(dto.Id);
        analysis.TargetAmount.Should().Be(0m);
        analysis.TargetDate.Should().BeNull();
        analysis.AccumulatedAmount.Should().Be(150m);
        analysis.RequiredMonthly.Should().Be(0m);
        analysis.MonthsRemaining.Should().Be(0);
        // With target 0, reachable is true
        analysis.TargetReachable.Should().BeTrue();

        // Sanity: postings sum in DB
        var sum = await db.Postings.AsNoTracking().Where(p => p.SavingsPlanId == dto.Id).SumAsync(p => p.Amount);
        sum.Should().Be(150m);

        conn.Dispose();
    }
}