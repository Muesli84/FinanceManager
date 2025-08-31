using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Auth;

public sealed class UserAdminServiceTests
{
    private static (UserAdminService sut, AppDbContext db, Mock<IPasswordHasher> hasher) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"HASH::{p}");
        var sut = new UserAdminService(db, hasher.Object);
        return (sut, db, hasher);
    }

    [Fact]
    public async Task CreateAsync_ShouldPersistUser()
    {
        var (sut, db, _) = Create();
        var dto = await sut.CreateAsync("alice", "Password1", true, CancellationToken.None);
        dto.Username.Should().Be("alice");
        dto.IsAdmin.Should().BeTrue();
        db.Users.Count().Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DuplicateUsername_Throws()
    {
        var (sut, db, _) = Create();
        db.Users.Add(new User("alice", "HASH::x", false));
        db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.CreateAsync("alice", "pw", false, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_Rename_WhenUnique_Works()
    {
        var (sut, db, _) = Create();
        var u = new User("old", "HASH::x", false);
        db.Users.Add(u); db.SaveChanges();
        var updated = await sut.UpdateAsync(u.Id, "new", null, null, null, CancellationToken.None);
        updated!.Username.Should().Be("new");
    }

    [Fact]
    public async Task UpdateAsync_Rename_ToExisting_Throws()
    {
        var (sut, db, _) = Create();
        var a = new User("a", "HASH::x", false);
        var b = new User("b", "HASH::x", false);
        db.Users.AddRange(a, b); db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.UpdateAsync(a.Id, "b", null, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesHash()
    {
        var (sut, db, hasher) = Create();
        var u = new User("user", "HASH::old", false);
        db.Users.Add(u); db.SaveChanges();
        hasher.Setup(h => h.Hash("newpw")).Returns("HASH::newpw");
        var ok = await sut.ResetPasswordAsync(u.Id, "newpw", CancellationToken.None);
        ok.Should().BeTrue();
        // Verify hash changed in tracked entity (reflection set) by reloading
        var re = db.Users.Single();
        re.PasswordHash.Should().Be("HASH::newpw");
    }

    [Fact]
    public async Task ResetPasswordAsync_Empty_Throws()
    {
        var (sut, db, _) = Create();
        var u = new User("user", "HASH::old", false);
        db.Users.Add(u); db.SaveChanges();
        await Assert.ThrowsAsync<ArgumentException>(() => sut.ResetPasswordAsync(u.Id, "", CancellationToken.None));
    }

    [Fact]
    public async Task UnlockAsync_ClearsLock()
    {
        var (sut, db, _) = Create();
        var u = new User("user", "HASH::x", false);
        // set lock (reflection via entity method)
        u.SetLockedUntil(DateTime.UtcNow.AddMinutes(10));
        db.Users.Add(u); db.SaveChanges();
        var ok = await sut.UnlockAsync(u.Id, CancellationToken.None);
        ok.Should().BeTrue();
        db.Users.Single().LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_RemovesUser()
    {
        var (sut, db, _) = Create();
        var u = new User("user", "HASH::x", false);
        db.Users.Add(u); db.SaveChanges();
        var ok = await sut.DeleteAsync(u.Id, CancellationToken.None);
        ok.Should().BeTrue();
        db.Users.Count().Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NonExisting_ReturnsFalse()
    {
        var (sut, db, _) = Create();
        var ok = await sut.DeleteAsync(Guid.NewGuid(), CancellationToken.None);
        ok.Should().BeFalse();
    }
}
