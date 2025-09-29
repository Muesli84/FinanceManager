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
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();
        var hasher = new Mock<IPasswordHasher>();
        var jwt = new Mock<IJwtTokenService>();
        var clock = new TestClock();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"HASH::{p}");
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((pw, hash) => hash == $"HASH::{pw}");
        jwt.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns("token");
        var logger = new Mock<ILogger<UserAuthService>>();
        var sut = new UserAuthService(db, hasher.Object, jwt.Object, clock, logger.Object);
        return (sut, db, hasher, jwt, clock);
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateFirstUserAsAdmin_WhenNoUsersExist()
    {
        var (sut, db, _, _, clock) = Create();
        var cmd = new RegisterUserCommand("alice", "Password123", null, null);
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
        var cmd = new RegisterUserCommand("bob", "pw", null, null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("exists");
    }

    [Fact]
    public async Task RegisterAsync_ShouldSetPreferredLanguage_WhenProvided()
    {
        var (sut, db, _, _, _) = Create();
        var cmd = new RegisterUserCommand("carol", "Password123", "en", null);
        var res = await sut.RegisterAsync(cmd, CancellationToken.None);

        res.Success.Should().BeTrue();
        var user = db.Users.Single(u => u.Username == "carol");
        user.PreferredLanguage.Should().Be("en");
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenUsernameOrPasswordMissing()
    {
        var (sut, _, _, _, _) = Create();

        var res1 = await sut.RegisterAsync(new RegisterUserCommand("", "pw", null, null), CancellationToken.None);
        var res2 = await sut.RegisterAsync(new RegisterUserCommand("user", "", null, null), CancellationToken.None);

        res1.Success.Should().BeFalse();
        res2.Success.Should().BeFalse();
        res1.Error.Should().Contain("required");
        res2.Error.Should().Contain("required");
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
        (user.LockedUntilUtc - clock.UtcNow).Should().BeGreaterThan(TimeSpan.FromMinutes(9));
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

    [Fact]
    public async Task LoginAsync_ShouldNotIncrementAttempts_WhileLocked()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("eve", "HASH::pw", false);
        user.IncrementFailedLoginAttempts(); // 1
        user.IncrementFailedLoginAttempts(); // 2
        user.IncrementFailedLoginAttempts(); // 3 -> wir simulieren Lock manuell
        user.SetLockedUntil(clock.UtcNow.AddMinutes(5));
        db.Users.Add(user);
        db.SaveChanges();

        var res = await sut.LoginAsync(new LoginCommand("eve", "wrong"), CancellationToken.None);

        res.Success.Should().BeFalse();
        res.Error.Should().Contain("locked");
        user.FailedLoginAttempts.Should().Be(3);
    }

    [Fact]
    public async Task LoginAsync_ShouldApplyFurtherEscalationDurations()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("dave", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("x", It.IsAny<string>())).Returns(false);

        // Attempts 1..3 -> lock after 3 (5 min)
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        var lock1End = user.LockedUntilUtc!;
        (lock1End - clock.UtcNow).Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5));

        // Advance & attempt 4 -> 10 min
        clock.UtcNow = lock1End.Value.AddSeconds(1);
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        var lock2End = user.LockedUntilUtc!;
        (lock2End - clock.UtcNow).Should().BeCloseTo(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5));

        // Advance & attempt 5 -> 20 min
        clock.UtcNow = lock2End.Value.AddSeconds(1);
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        var lock3End = user.LockedUntilUtc!;
        (lock3End - clock.UtcNow).Should().BeCloseTo(TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(5));

        // Advance & attempt 6 -> 40 min
        clock.UtcNow = lock3End.Value.AddSeconds(1);
        await sut.LoginAsync(new LoginCommand("dave", "x"), CancellationToken.None);
        var lock4End = user.LockedUntilUtc!;
        (lock4End - clock.UtcNow).Should().BeCloseTo(TimeSpan.FromMinutes(40), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LoginAsync_LockDuration_ShouldBeCappedAtEightHours()
    {
        var (sut, db, hasher, _, clock) = Create();
        var user = new User("zoe", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        hasher.Setup(h => h.Verify("bad", It.IsAny<string>())).Returns(false);

        // Drive failures until duration would exceed 8h (cap)
        // We reuse helper: perform wrong attempts with advancing time
        async Task FailAndAdvanceAsync()
        {
            var res = await sut.LoginAsync(new LoginCommand("zoe", "bad"), CancellationToken.None);
            res.Success.Should().BeFalse();
            if (user.LockedUntilUtc != null && user.LockedUntilUtc > clock.UtcNow)
            {
                clock.UtcNow = user.LockedUntilUtc.Value.AddSeconds(1);
            }
        }

        // Attempts loop until failed attempts >= 11 (would exceed 8h without cap)
        while (user.FailedLoginAttempts < 11)
        {
            await FailAndAdvanceAsync();
        }

        // Last lock set after attempt 11
        user.LockedUntilUtc.Should().NotBeNull();
        var duration = user.LockedUntilUtc!.Value - clock.UtcNow;
        duration.Should().BeLessThanOrEqualTo(TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(1)));
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }
}
