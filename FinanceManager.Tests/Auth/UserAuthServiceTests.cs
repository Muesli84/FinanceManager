using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FinanceManager.Tests.Auth;

public sealed class UserAuthServiceTests
{
    private static (UserAuthService sut, AppDbContext db, Mock<UserManager<User>> userManager, Mock<SignInManager<User>> signInManager, Mock<IJwtTokenService> jwt, TestClock clock) Create()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null, null, null, null, null, null, null, null);

        var httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<User>>();
        var options = Options.Create(new IdentityOptions());
        var loggerSignInMock = new Mock<ILogger<SignInManager<User>>>();
        var schemesMock = new Mock<IAuthenticationSchemeProvider>();
        var confirmationMock = new Mock<IUserConfirmation<User>>();

        var signInManagerMock = new Mock<SignInManager<User>>(userManagerMock.Object, httpContextAccessorMock.Object, claimsFactoryMock.Object, options, loggerSignInMock.Object, schemesMock.Object, confirmationMock.Object);

        // default: only password 'pw' is valid for check via PasswordSignInAsync
        signInManagerMock.Setup(s => s.PasswordSignInAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .ReturnsAsync((User user, string pwd, bool pers, bool lockout) => pwd == "pw" ? SignInResult.Success : SignInResult.Failed);

        userManagerMock.Setup(u => u.CheckPasswordAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync((User user, string pwd) => pwd == "pw");

        userManagerMock.Setup(u => u.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var jwt = new Mock<IJwtTokenService>();
        var clock = new TestClock();

        jwt.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), out It.Ref<DateTime>.IsAny, It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns("token");

        var logger = new Mock<ILogger<UserAuthService>>();

        // password hasher mock
        var passwordHasherMock = new Mock<IPasswordHashingService>();
        passwordHasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns<string>(p => $"HASH::{p}");

        // prepare a mock RoleManager so first-user role assignment can run in tests
        var roleStoreMock = new Mock<IRoleStore<IdentityRole<Guid>>>();
        var roleValidators = new List<IRoleValidator<IdentityRole<Guid>>>();
        var lookupNormalizerMock = new Mock<ILookupNormalizer>();
        var roleLoggerMock = new Mock<ILogger<RoleManager<IdentityRole<Guid>>>>();

        // Use an in-memory role set and configure the role store to operate on it. Create a real RoleManager instance
        // backed by this mocked IRoleStore. This avoids Moq trying to proxy RoleManager's constructor.
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lookupNormalizerMock.Setup(n => n.NormalizeName(It.IsAny<string>())).Returns((string s) => s?.ToUpperInvariant());

        roleStoreMock
            .Setup(s => s.FindByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string normalizedName, CancellationToken ct) =>
            {
                if (normalizedName != null && roles.Contains(normalizedName))
                {
                    return new IdentityRole<Guid> { Id = Guid.NewGuid(), Name = normalizedName, NormalizedName = normalizedName };
                }
                return null;
            });

        roleStoreMock
            .Setup(s => s.CreateAsync(It.IsAny<IdentityRole<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<IdentityRole<Guid>, CancellationToken>((role, ct) =>
            {
                if (role == null) return;
                var n = role.NormalizedName ?? role.Name?.ToUpperInvariant();
                if (!string.IsNullOrEmpty(n)) roles.Add(n);
            });

        // RoleManager constructor in .NET 9 has five parameters: store, roleValidators, lookupNormalizer, errors, logger
        var roleManager = new RoleManager<IdentityRole<Guid>>(roleStoreMock.Object, roleValidators, lookupNormalizerMock.Object, new IdentityErrorDescriber(), roleLoggerMock.Object);

        // maintain user->roles mapping for UserManager mock
        var userRoles = new Dictionary<Guid, HashSet<string>>();

        userManagerMock
            .Setup(um => um.AddToRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User, string>((user, role) =>
            {
                if (user == null || string.IsNullOrEmpty(role)) return;
                if (!userRoles.TryGetValue(user.Id, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    userRoles[user.Id] = set;
                }
                set.Add(role);
                // also ensure role exists in role store normalized set
                roles.Add(role.ToUpperInvariant());
            });

        userManagerMock
            .Setup(um => um.RemoveFromRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success)
            .Callback<User, string>((user, role) =>
            {
                if (user == null || string.IsNullOrEmpty(role)) return;
                if (userRoles.TryGetValue(user.Id, out var set))
                {
                    set.Remove(role);
                    if (set.Count == 0) userRoles.Remove(user.Id);
                }
            });

        userManagerMock
            .Setup(um => um.IsInRoleAsync(It.IsAny<User>(), It.IsAny<string>()))
            .ReturnsAsync((User user, string role) =>
            {
                if (user == null || string.IsNullOrEmpty(role)) return false;
                return userRoles.TryGetValue(user.Id, out var set) && set.Contains(role);
            });

        // pass signInManagerMock.Object and real roleManager to service
        var sut = new UserAuthService(db, userManagerMock.Object, signInManagerMock.Object, jwt.Object, passwordHasherMock.Object, clock, logger.Object, new FinanceManager.Infrastructure.Auth.UserAuthService.NoopIpBlockService(), roleManager);
        return (sut, db, userManagerMock, signInManagerMock, jwt, clock);
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateFirstUserAsAdmin_WhenNoUsersExist()
    {
        var (sut, db, _, _, _, clock) = Create();
        var cmd = new RegisterUserCommand("alice", "Password123", null, null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Value!.IsAdmin);
        Assert.Equal(1, db.Users.Count());
        Assert.True(db.Users.Single().IsAdmin);
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenDuplicateUsername()
    {
        var (sut, db, _, _, _, _) = Create();
        db.Users.Add(new User("bob", "HASH::x", false));
        db.SaveChanges();
        var cmd = new RegisterUserCommand("bob", "pw", null, null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("exists", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAsync_ShouldSetPreferredLanguage_WhenProvided()
    {
        var (sut, db, _, _, _, _) = Create();
        var cmd = new RegisterUserCommand("carol", "Password123", "en", null);
        var res = await sut.RegisterAsync(cmd, CancellationToken.None);

        Assert.True(res.Success);
        var user = db.Users.Single(u => u.UserName == "carol");
        Assert.Equal("en", user.PreferredLanguage);
    }

    [Fact]
    public async Task RegisterAsync_ShouldFail_WhenUsernameOrPasswordMissing()
    {
        var (sut, _, _, _, _, _) = Create();

        var res1 = await sut.RegisterAsync(new RegisterUserCommand("", "pw", null, null), CancellationToken.None);
        var res2 = await sut.RegisterAsync(new RegisterUserCommand("user", "", null, null), CancellationToken.None);

        Assert.False(res1.Success);
        Assert.False(res2.Success);
        Assert.Contains("required", res1.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required", res2.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_FirstAndSecondInvalid_NoLock()
    {
        var (sut, db, userManager, signInManager, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        signInManager.Setup(s => s.PasswordSignInAsync(user, "wrong", false, true)).ReturnsAsync(SignInResult.Failed);
        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        // first invalid
        var r1 = await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        Assert.False(r1.Success);

        // second invalid
        var r2 = await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        Assert.False(r2.Success);
    }

    [Fact]
    public async Task LoginAsync_ThirdInvalid_LeadsToIdentityLockout()
    {
        var (sut, db, userManager, signInManager, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();

        // simulate: first two attempts fail, third attempt triggers Identity lockout → SignInResult.LockedOut
        signInManager.SetupSequence(s => s.PasswordSignInAsync(user, It.IsAny<string>(), false, true))
            .ReturnsAsync(SignInResult.Failed)
            .ReturnsAsync(SignInResult.Failed)
            .ReturnsAsync(SignInResult.LockedOut);

        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        var res = await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);

        Assert.False(res.Success);
        Assert.Contains("locked", res.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_Success_ResetsIdentityLockout()
    {
        var (sut, db, userManager, signInManager, _, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();

        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        var success = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        Assert.True(success.Success);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnToken_OnValidCredentials()
    {
        var (sut, db, userManager, signInManager, jwt, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        var res = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        Assert.True(res.Success);
        Assert.Equal("token", res.Value!.Token);
    }

    [Fact]
    public async Task LoginAsync_ShouldSucceed_AfterIdentityLockExpired_AndValidCredentials()
    {
        var (sut, db, userManager, signInManager, jwt, clock) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user);
        db.SaveChanges();

        // simulate lockout then expiry: first produce LockedOut, then after time password success
        signInManager.SetupSequence(s => s.PasswordSignInAsync(user, It.IsAny<string>(), false, true))
            .ReturnsAsync(SignInResult.LockedOut)
            .ReturnsAsync(SignInResult.Success);

        var r1 = await sut.LoginAsync(new LoginCommand("bob", "wrong"), CancellationToken.None);
        Assert.False(r1.Success);

        var r2 = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        Assert.True(r2.Success);
        Assert.Equal("token", r2.Value!.Token);
    }

    [Fact]
    public async Task LoginAsync_ShouldNotIncrementAttempts_WhileLocked()
    {
        var (sut, db, userManager, signInManager, _, clock) = Create();
        var user = new User("eve", "HASH::pw", false);
        db.Users.Add(user);
        db.SaveChanges();

        // simulate locked state -> PasswordSignInAsync returns LockedOut
        signInManager.Setup(s => s.PasswordSignInAsync(user, It.IsAny<string>(), false, true)).ReturnsAsync(SignInResult.LockedOut);

        var res = await sut.LoginAsync(new LoginCommand("eve", "wrong"), CancellationToken.None);
        Assert.False(res.Success);
        Assert.Contains("locked", res.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    }
}
