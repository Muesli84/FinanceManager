using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class HomeKpiServiceTests
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
    public async Task Create_List_Update_Delete_ShouldWork_WithOwnershipChecks()
    {
        using var db = CreateDb();
        var user1 = new FinanceManager.Domain.Users.User("u1","pw", false);
        var user2 = new FinanceManager.Domain.Users.User("u2","pw", false);
        db.Users.AddRange(user1,user2); await db.SaveChangesAsync();
        var fav1 = new ReportFavorite(user1.Id, "Fav1", 1, false, ReportInterval.Month, false, false, false, true);
        db.ReportFavorites.Add(fav1); await db.SaveChangesAsync();

        var svc = new HomeKpiService(db);

        // create predefined
        var k1 = await svc.CreateAsync(user1.Id, new HomeKpiCreateRequest(HomeKpiKind.Predefined, null, HomeKpiDisplayMode.TotalOnly, 0), CancellationToken.None);
        k1.Kind.Should().Be(HomeKpiKind.Predefined);

        // create favorite
        var k2 = await svc.CreateAsync(user1.Id, new HomeKpiCreateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.TotalWithComparisons, 1), CancellationToken.None);
        k2.ReportFavoriteId.Should().Be(fav1.Id);

        // list
        var list = await svc.ListAsync(user1.Id, CancellationToken.None);
        list.Should().HaveCount(2);

        // update
        var upd = await svc.UpdateAsync(k2.Id, user1.Id, new HomeKpiUpdateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.ReportGraph, 3), CancellationToken.None);
        upd!.DisplayMode.Should().Be(HomeKpiDisplayMode.ReportGraph);
        upd.SortOrder.Should().Be(3);

        // ownership check on favorite
        await FluentActions.Invoking(() => svc.CreateAsync(user2.Id, new HomeKpiCreateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.TotalOnly, 0), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        // delete
        (await svc.DeleteAsync(k1.Id, user1.Id, CancellationToken.None)).Should().BeTrue();
        (await svc.DeleteAsync(Guid.NewGuid(), user1.Id, CancellationToken.None)).Should().BeFalse();
    }
}
