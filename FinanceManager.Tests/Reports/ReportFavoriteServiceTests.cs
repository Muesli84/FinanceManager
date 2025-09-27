using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports; // ReportInterval
using FinanceManager.Domain; // PostingKind
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Reports;
using FluentAssertions;
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

        var dto = await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("MyFav",  (int)PostingKind.Contact, true, ReportInterval.Month, true, false, true, true), CancellationToken.None);
        dto.Id.Should().NotBeEmpty();
        dto.Name.Should().Be("MyFav");
        dto.IncludeCategory.Should().BeTrue();
        dto.Interval.Should().Be(ReportInterval.Month);

        var entity = await db.ReportFavorites.FirstAsync();
        entity.Name.Should().Be("MyFav");
    }

    [Fact]
    public async Task CreateAsync_ShouldThrow_OnDuplicateNamePerUser()
    {
        using var db = CreateDb();
        var user = new FinanceManager.Domain.Users.User("user","pw", false);
        db.Users.Add(user); await db.SaveChangesAsync();
        var svc = new ReportFavoriteService(db);
        await svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("Dup", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None);
        await FluentActions.Invoking(() => svc.CreateAsync(user.Id, new ReportFavoriteCreateRequest("Dup", 1, false, ReportInterval.Month, false, false, false, false), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
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
        (await db.ReportFavorites.CountAsync()).Should().Be(2);
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
        await FluentActions.Invoking(() => svc.UpdateAsync(a.Id, user.Id, new ReportFavoriteUpdateRequest("B", 2, true, ReportInterval.Year, true, true, true, true), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        // Valid update
        var updated = await svc.UpdateAsync(a.Id, user.Id, new ReportFavoriteUpdateRequest("A-Updated", 2, true, ReportInterval.Year, true, true, true, true), CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("A-Updated");
        updated.PostingKind.Should().Be(2);
        updated.IncludeCategory.Should().BeTrue();
        updated.Interval.Should().Be(ReportInterval.Year);
        updated.ComparePrevious.Should().BeTrue();
        updated.CompareYear.Should().BeTrue();
        updated.ShowChart.Should().BeTrue();
        updated.Expandable.Should().BeTrue();
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
        (await svc.DeleteAsync(fav.Id, user2.Id, CancellationToken.None)).Should().BeFalse();
        (await svc.DeleteAsync(Guid.NewGuid(), user1.Id, CancellationToken.None)).Should().BeFalse();
        (await svc.DeleteAsync(fav.Id, user1.Id, CancellationToken.None)).Should().BeTrue();
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
        list1.Select(l=>l.Name).Should().ContainInOrder("Alpha","Zeta"); // ordered by name
        list1.Should().HaveCount(2);

        var list2 = await svc.ListAsync(user2.Id, CancellationToken.None);
        list2.Should().HaveCount(1).And.OnlyContain(f => f.Name == "Other");

        var first = list1.First();
        var fetched = await svc.GetAsync(first.Id, user1.Id, CancellationToken.None);
        fetched!.Name.Should().Be(first.Name);
        (await svc.GetAsync(first.Id, user2.Id, CancellationToken.None)).Should().BeNull();
    }
}
