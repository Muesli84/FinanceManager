using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FinanceManager.Domain.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class HomeKpiTests
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
    public async Task Create_HomeKpi_ForFavorite_ShouldRequireFavoriteId()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();

        var fav = new ReportFavorite(user.Id, "Fav", 1, false, ReportInterval.Month, false, false, false, true);
        db.ReportFavorites.Add(fav); await db.SaveChangesAsync();

        // valid
        var kpi = new HomeKpi(user.Id, HomeKpiKind.ReportFavorite, HomeKpiDisplayMode.TotalOnly, sortOrder: 0, reportFavoriteId: fav.Id);
        db.HomeKpis.Add(kpi);
        await db.SaveChangesAsync();

        // invalid: missing favorite id
        var act = () => { var invalid = new HomeKpi(user.Id, HomeKpiKind.ReportFavorite, HomeKpiDisplayMode.TotalOnly, sortOrder: 1, reportFavoriteId: null); };
        Assert.Throws<ArgumentException>(act);
    }

    [Fact]
    public async Task CascadeDelete_Favorite_ShouldRemoveRelatedHomeKpis()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("owner","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var fav = new ReportFavorite(user.Id, "Fav", 1, false, ReportInterval.Month, false, false, false, true);
        db.ReportFavorites.Add(fav); await db.SaveChangesAsync();

        db.HomeKpis.Add(new HomeKpi(user.Id, HomeKpiKind.ReportFavorite, HomeKpiDisplayMode.TotalOnly, 0, fav.Id));
        db.HomeKpis.Add(new HomeKpi(user.Id, HomeKpiKind.ReportFavorite, HomeKpiDisplayMode.TotalWithComparisons, 1, fav.Id));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.HomeKpis.CountAsync());

        db.ReportFavorites.Remove(fav);
        await db.SaveChangesAsync();

        Assert.Equal(0, await db.HomeKpis.CountAsync());
    }
}
