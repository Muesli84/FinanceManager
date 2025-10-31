using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Savings;
using FinanceManager.Domain;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class SavingsPlanAnalysisTests
{
    private static (SavingsPlanService sut, AppDbContext db, SqliteConnection conn, Guid owner) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(conn).Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        var sut = new SavingsPlanService(db);
        var owner = Guid.NewGuid();
        return (sut, db, conn, owner);
    }

    private static async Task<Guid> CreatePlanAsync(AppDbContext db, Guid owner, string name, decimal target, DateTime targetDate)
    {
        var plan = new FinanceManager.Domain.Savings.SavingsPlan(owner, name, SavingsPlanType.OneTime, target, targetDate, null, null);
        db.SavingsPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private static async Task AddPlanPostingAsync(AppDbContext db, Guid planId, DateTime date, decimal amount)
    {
        var p = new FinanceManager.Domain.Postings.Posting(Guid.NewGuid(), PostingKind.SavingsPlan, null, null, planId, null, date, amount, null, null, null, null);
        db.Postings.Add(p);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Scenario1_ThreePastMonths10_Reacheable()
    {
        var (sut, db, conn, owner) = Create();
        var planId = await CreatePlanAsync(db, owner, "P", 120m, DateTime.Today.AddMonths(9));
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-1), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-2), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-3), 10m);

        var result = await sut.AnalyzeAsync(planId, owner, CancellationToken.None);
        Assert.True(result.TargetReachable);
        conn.Dispose();
    }

    [Fact]
    public async Task Scenario2_TwoPastMonths10_NotReacheable()
    {
        var (sut, db, conn, owner) = Create();
        var planId = await CreatePlanAsync(db, owner, "P", 120m, DateTime.Today.AddMonths(9));
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-1), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-2), 10m);

        var result = await sut.AnalyzeAsync(planId, owner, CancellationToken.None);
        Assert.False(result.TargetReachable);
        conn.Dispose();
    }

    [Fact]
    public async Task Scenario3_TwoMonthsAgo10_AndLastMonth10_Reachable()
    {
        var (sut, db, conn, owner) = Create();
        var planId = await CreatePlanAsync(db, owner, "P", 120m, DateTime.Today.AddMonths(9));
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-2), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-2).AddDays(1), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-1), 10m);

        var result = await sut.AnalyzeAsync(planId, owner, CancellationToken.None);
        Assert.True(result.TargetReachable);
        conn.Dispose();
    }

    [Fact]
    public async Task Scenario4_FourPastMonths10_Reachable()
    {
        var (sut, db, conn, owner) = Create();
        var planId = await CreatePlanAsync(db, owner, "P", 120m, DateTime.Today.AddMonths(9));
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-1), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-2), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-3), 10m);
        await AddPlanPostingAsync(db, planId, DateTime.Today.AddMonths(-4), 10m);

        var result = await sut.AnalyzeAsync(planId, owner, CancellationToken.None);
        Assert.True(result.TargetReachable);
        conn.Dispose();
    }
}
