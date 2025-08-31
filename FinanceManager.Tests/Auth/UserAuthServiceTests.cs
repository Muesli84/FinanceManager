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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FinanceManager.Tests.Auth;

public sealed class UserAuthServiceTests
{
    private static (UserAuthService sut, AppDbContext db, Mock<IPasswordHasher> hasher, Mock<IJwtTokenService> jwt, TestClock clock) Create()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // ensures ILogger<T> infrastructure if needed
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
        var logger = new Mock<ILogger<UserAuthService>>();
        var sut = new UserAuthService(db, hasher.Object, jwt.Object, clock, logger.Object);
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
    public async Task LoginAsync_FirstAndSecondInvalid_NoLock()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        user.FailedLoginAttempts.Should().Be(1);
        user.LockedUntilUtc.Should().BeNull();

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        user.FailedLoginAttempts.Should().Be(2);
        user.LockedUntilUtc.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_ThirdInvalid_LocksUser()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);

        user.FailedLoginAttempts.Should().Be(3);
        user.LockedUntilUtc.Should().NotBeNull();
        (user.LockedUntilUtc - clock.UtcNow).Should().BeGreaterThan(TimeSpan.FromMinutes(4)).And.BeLessThan(TimeSpan.FromMinutes(6));
    }

    [Fact]
    public async Task LoginAsync_AfterLockExpires_FurtherInvalidsEscalateLock()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        var firstLockExpiry = user.LockedUntilUtc!;

        clock.UtcNow = firstLockExpiry.Value.AddSeconds(1);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        user.FailedLoginAttempts.Should().Be(4);
        user.LockedUntilUtc.Should().NotBeNull();
        (user.LockedUntilUtc - clock.UtcNow).Should().BeGreaterThan(TimeSpan.FromMinutes(9)); // ~10 minutes
    }

    [Fact]
    public async Task LoginAsync_Success_ResetsCounterAndLock()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);
        hasher.Setup(h => h.Verify("pw", It.IsAny<string>())).Returns(true);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);

        var success = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        success.Success.Should().BeTrue();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntilUtc.Should().BeNull();
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

    [Fact]
    public async Task LoginAsync_ShouldSucceed_AfterLockExpired_AndValidCredentials()
    {
        var (sut, db, hasher, jwt, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user);
        db.SaveChanges();

        hasher.Setup(h => h.Verify("wrong", It.IsAny<string>())).Returns(false);
        hasher.Setup(h => h.Verify("pw", It.IsAny<string>())).Returns(true);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);

        user.LockedUntilUtc.Should().NotBeNull();
        var lockedUntil = user.LockedUntilUtc!.Value;

        clock.UtcNow = lockedUntil.AddSeconds(1);

        var result = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Value!.Token.Should().Be("token");
        user.LockedUntilUtc.Should().BeNull();
        user.FailedLoginAttempts.Should().Be(0);
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }
}
