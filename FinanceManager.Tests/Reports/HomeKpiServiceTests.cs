using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Shared.Dtos;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using FinanceManager.Domain.Reports;

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
        Assert.Equal(HomeKpiKind.Predefined, k1.Kind);

        // create favorite
        var k2 = await svc.CreateAsync(user1.Id, new HomeKpiCreateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.TotalWithComparisons, 1), CancellationToken.None);
        Assert.Equal(fav1.Id, k2.ReportFavoriteId);

        // list
        var list = await svc.ListAsync(user1.Id, CancellationToken.None);
        Assert.Equal(2, list.Count);

        // update
        var upd = await svc.UpdateAsync(k2.Id, user1.Id, new HomeKpiUpdateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.ReportGraph, 3), CancellationToken.None);
        Assert.Equal(HomeKpiDisplayMode.ReportGraph, upd!.DisplayMode);
        Assert.Equal(3, upd.SortOrder);

        // ownership check on favorite
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(user2.Id, new HomeKpiCreateRequest(HomeKpiKind.ReportFavorite, fav1.Id, HomeKpiDisplayMode.TotalOnly, 0), CancellationToken.None));

        // delete
        Assert.True(await svc.DeleteAsync(k1.Id, user1.Id, CancellationToken.None));
        Assert.False(await svc.DeleteAsync(Guid.NewGuid(), user1.Id, CancellationToken.None));
    }
}
