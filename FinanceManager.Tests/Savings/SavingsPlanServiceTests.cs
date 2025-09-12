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
}