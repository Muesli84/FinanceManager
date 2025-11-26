using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports; // ReportInterval
using FinanceManager.Domain; // PostingKind
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceManager.Tests.Reports;

public sealed class ReportFavoriteServiceTests
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
    public async Task CreateAsync_ShouldPersistAndReturnDto()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("user","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);

        var dto = await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("MyFav",  PostingKind.Contact, true, ReportInterval.Month, true, false, true, true), CancellationToken.None);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("MyFav", dto.Name);
        Assert.True(dto.IncludeCategory);
        Assert.Equal(ReportInterval.Month, dto.Interval);

        var entity = await db.ReportFavorites.FirstAsync();
        Assert.Equal("MyFav", entity.Name);
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_OnDuplicateNamePerUser()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("user","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("Dup", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("Dup", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ShouldAllowSameNameForDifferentUsers()
    {
        using var db = CreateDb();
        var user1 = new FinanceManager.Domain.Users.User("u1","pw", false);
        var user2 = new FinanceManager.Domain.Users.User("u2","pw", false);
        db.Users.AddRange(user1,user2); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        await svc.CreateAsync(user1.Id, new ReportFavoriteCreateRequest("Same", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        await svc.CreateAsync(user2.Id, new ReportFavoriteCreateRequest("Same", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        Assert.Equal(2, await db.ReportFavorites.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_ShouldModifyFields_AndRejectDuplicate()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("u","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        var a = await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("A", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        var b = await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("B", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);

        // Duplicate rename attempt
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.UpdateAsync(a.Id, user.Id, new ReportFavoriteUpdateRequest("B", 2, true, ReportInterval.Year, true, true, true, true), CancellationToken.None));

        // Valid update
        var updated = await svc.UpdateAsync(a.Id, user.Id, new ReportFavoriteUpdateRequest("A-Updated", 2, true, ReportInterval.Year, true, true, true, true), CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("A-Updated", updated!.Name);
        Assert.Equal(2, updated.PostingKind);
        Assert.True(updated.IncludeCategory);
        Assert.Equal(ReportInterval.Year, updated.Interval);
        Assert.True(updated.ComparePrevious);
        Assert.True(updated.CompareYear);
        Assert.True(updated.ShowChart);
        Assert.True(updated.Expandable);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenNotOwnedOrMissing()
    {
        using var db = CreateDb();
        var user1 = new FinanceManager.Domain.Users.User("u1","pw", false);
        var user2 = new FinanceManager.Domain.Users.User("u2","pw", false);
        db.Users.AddRange(user1,user2); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        var fav = await svc.CreateAsync(user1.Id, new ReportFavoriteCreateRequest("Fav", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        Assert.False(await svc.DeleteAsync(fav.Id, user2.Id, CancellationToken.None));
        Assert.False(await svc.DeleteAsync(Guid.NewGuid(), user1.Id, CancellationToken.None));
        Assert.True(await svc.DeleteAsync(fav.Id, user1.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListAndGet_ShouldRespectOwnershipAndOrdering()
    {
        using var db = CreateDb();
        var user1 = new FinanceManager.Domain.Users.User("u1","pw", false);
        var user2 = new FinanceManager.Domain.Users.User("u2","pw", false);
        db.Users.AddRange(user1,user2); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        await svc.CreateAsync(user1.Id, new ReportFavoriteCreateRequest("Zeta", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        await svc.CreateAsync(user1.Id, new ReportFavoriteCreateRequest("Alpha", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        await svc.CreateAsync(user2.Id, new ReportFavoriteCreateRequest("Other", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);

        var list1 = await svc.ListAsync(user1.Id, CancellationToken.None);
        var names = list1.Select(l => l.Name).ToArray();
        Assert.Equal(new[] { "Alpha", "Zeta" }, names); // ordered by name
        Assert.Equal(2, list1.Count);

        var list2 = await svc.ListAsync(user2.Id, CancellationToken.None);
        Assert.Equal(1, list2.Count);
        Assert.All(list2, f => Assert.Equal("Other", f.Name));

        var first = list1.First();
        var fetched = await svc.GetAsync(first.Id, user1.Id, CancellationToken.None);
        Assert.Equal(first.Name, fetched!.Name);
        Assert.Null(await svc.GetAsync(first.Id, user2.Id, CancellationToken.None));
    }
}
