using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
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

public sealed class UserAuthServiceTests
{
    private static (UserAuthService sut, AppDbContext db, Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt, TestClock clock) Create()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var hasher = new Mock<IPasswordHasher>();
        var jwt = new Mock<IJwtTokenService>();
        var clock = new TestClock();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"HASH::{p}");
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((pw, hash) => hash == $"HASH::{pw}");
        jwt.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTime>()))
            .Returns("token");
        var sut = new UserAuthService(db, hasher.Object, jwt.Object, clock);
        return (sut, db, hasher, jwt, clock);
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateFirstUserAsAdmin_WhenNoUsersExist()
    {
        var (sut, db, _, _, clock) = Create();
        var cmd = new RegisterUserCommand("alice", "Password123", null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.IsAdmin.Should().BeTrue();
        db.Users.Count().Should().Be(1);
        db.Users.Single().IsAdmin.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenDuplicateUsername()
    {
        var (sut, db, _, _, _) = Create();
        db.Users.Add(new User("bob", "HASH::x", false));
        db.SaveChanges();
        var cmd = new RegisterUserCommand("bob", "pw", null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exists");
    }

    [Fact]
    public async Task LoginAsync_ShouldLockUser_OnInvalidPassword()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);

        var res = await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        res.Success.Should().BeFalse();
        user.LockedUntilUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_OnValidCredentials()
    {
        var (sut, db, hasher, jwt, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("pw", It.IsAny<string>())).Returns(true);

        var res = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        res.Success.Should().BeTrue();
        res.Value!.Token.Should().Be("token");
        user.LockedUntilUtc.Should().BeNull();
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }
}
