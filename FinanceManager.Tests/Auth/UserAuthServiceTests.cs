using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Auth;
using FinanceManager.Shared.Dtos.Admin;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace FinanceManager.Tests.Auth;

/// <summary>
/// Tests for <see cref="UserAuthService"/> covering self-service registration and login: the
/// first-user-becomes-admin bootstrap rule, username/password validation, the Identity lockout
/// progression across repeated failed attempts, JWT issuance on success, rejection of deactivated
/// accounts, and the rule that a browser-reported language hint must never overwrite a user's
/// existing preference.
/// </summary>
public sealed class UserAuthServiceTests
{
    private static (UserAuthService sut, AppDbContext db, Mock<UserManager<User>> userManager, Mock<SignInManager<User>> signInManager, Mock<IJwtTokenService> jwt, TimeProvider timeProvider) Create(IBackgroundTaskManager? backgroundTaskManager = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        var sp = services.BuildServiceProvider();
        var db = sp.GetRequiredService<AppDbContext>();

        var store = new Mock<IUserStore<User>>();
        var userManagerMock = new Mock<UserManager<User>>(store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

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
        var timeProvider = TimeProvider.System;

        jwt.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), out It.Ref<DateTime>.IsAny, It.IsAny<string?>(), It.IsAny<string?>()))
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

        // Mirrors the real ASP.NET Core Identity default normalizer, which tolerates a null input (returns
        // null) - Identity's internal machinery (e.g. role/user store operations during first-user admin
        // bootstrap) can call this with null, and RegisterAsync's role-assignment try/catch would silently
        // swallow a resulting NullReferenceException here, masking the failure as "first user just isn't admin".
        lookupNormalizerMock.Setup(n => n.NormalizeName(It.IsAny<string>())).Returns((string s) => (s?.ToUpperInvariant())!);

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
        var sut = new UserAuthService(
            db,
            userManagerMock.Object,
            signInManagerMock.Object,
            jwt.Object,
            passwordHasherMock.Object,
            timeProvider,
            logger.Object,
            new FinanceManager.Infrastructure.Auth.UserAuthService.NoopIpBlockService(),
            roleManager,
            backgroundTaskManager);
        return (sut, db, userManagerMock, signInManagerMock, jwt, timeProvider);
    }

    /// <summary>
    /// Verifies that when no users exist yet, the very first registered account is automatically
    /// granted the admin role - the bootstrap mechanism that guarantees a fresh installation always
    /// has at least one administrator without requiring a separate seeding step.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ShouldCreateFirstUserAsAdmin_WhenNoUsersExist()
    {
        var (sut, db, _, _, _, _) = Create();
        var cmd = new RegisterUserCommand("alice", "Password123", null, null);
        var result = await sut.RegisterAsync(cmd, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Value!.IsAdmin);
        Assert.Equal(1, db.Users.Count());
        Assert.True(db.Users.Single().IsAdmin);
    }

    /// <summary>
    /// Verifies that self-registration with a username that already exists fails with an
    /// error mentioning the conflict, rather than silently creating a duplicate account.
    /// </summary>
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

    /// <summary>
    /// Verifies that an explicit preferred-language value supplied at registration is persisted on
    /// the new user, so the account starts in that language rather than the default "Automatic" mode.
    /// </summary>
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

    /// <summary>
    /// Verifies that registration rejects a missing username and, separately, a missing password,
    /// each with an error explaining the field is required, rather than proceeding with an
    /// incomplete account.
    /// </summary>
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

    /// <summary>
    /// Verifies that a first registration with <c>CreateDemoData=true</c> enqueues exactly one
    /// background task of type <see cref="BackgroundTaskType.CreateDemoData"/>.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ShouldQueueDemoData_WhenFirstUserAndCreateDemoDataEnabled()
    {
        var backgroundTaskManager = new BackgroundTaskManager();
        var (sut, _, _, _, _, _) = Create(backgroundTaskManager);

        var result = await sut.RegisterAsync(new RegisterUserCommand("first-demo", "Password123", null, null, true), CancellationToken.None);

        Assert.True(result.Success);
        var tasks = backgroundTaskManager.GetAll();
        Assert.Single(tasks);
        Assert.Equal(BackgroundTaskType.CreateDemoData, tasks[0].Type);
        Assert.Equal(result.Value!.UserId, tasks[0].UserId);
        Assert.Equal(BackgroundTaskStatus.Queued, tasks[0].Status);
    }

    /// <summary>
    /// Verifies that the demo-data enqueue flag is ignored for non-first registrations even when
    /// the caller sends <c>CreateDemoData=true</c>.
    /// </summary>
    [Fact]
    public async Task RegisterAsync_ShouldNotQueueDemoData_WhenNotFirstUser()
    {
        var backgroundTaskManager = new BackgroundTaskManager();
        var (sut, db, _, _, _, _) = Create(backgroundTaskManager);

        db.Users.Add(new User("existing-user", "HASH::existing", false));
        db.SaveChanges();

        var result = await sut.RegisterAsync(new RegisterUserCommand("second-demo", "Password123", null, null, true), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Empty(backgroundTaskManager.GetAll());
    }

    /// <summary>
    /// Verifies that the first two consecutive failed login attempts are simply rejected without
    /// triggering a lockout, confirming the lockout threshold is not tripped prematurely.
    /// </summary>
    [Fact]
    public async Task LoginAsync_FirstAndSecondInvalid_NoLock()
    {
        var (sut, db, userManager, signInManager, _, _) = Create();
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

    /// <summary>
    /// Verifies that a third consecutive failed attempt escalates to Identity's lockout state and
    /// the login result surfaces a "locked" error, confirming brute-force protection actually
    /// engages after the configured number of failures.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ThirdInvalid_LeadsToIdentityLockout()
    {
        var (sut, db, userManager, signInManager, _, _) = Create();
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

    /// <summary>
    /// Verifies that a successful login with correct credentials simply succeeds, exercising the
    /// happy path that implicitly resets Identity's internal failure counter for the account.
    /// </summary>
    [Fact]
    public async Task LoginAsync_Success_ResetsIdentityLockout()
    {
        var (sut, db, userManager, signInManager, _, _) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();

        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        var success = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        Assert.True(success.Success);
    }

    /// <summary>
    /// Verifies that a successful login returns the JWT produced by <c>IJwtTokenService</c> and
    /// that the token is requested with the user's id, username, security stamp, language, and
    /// time zone - confirming the token creation call is wired with the correct claims source.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ShouldReturnToken_OnValidCredentials()
    {
        var (sut, db, userManager, signInManager, jwt, _) = Create();
        var user = new User("bob", "HASH::pw", false);
        db.Users.Add(user); db.SaveChanges();
        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        var res = await sut.LoginAsync(new LoginCommand("bob", "pw"), CancellationToken.None);
        Assert.True(res.Success);
        Assert.Equal("token", res.Value!.Token);
        jwt.Verify(j => j.CreateToken(user.Id, user.UserName!, false, user.SecurityStamp!, out It.Ref<DateTime>.IsAny, user.PreferredLanguage, user.TimeZoneId), Times.Once);
    }

    /// <summary>
    /// Verifies that logging in as a deactivated user fails with a generic "invalid credentials"
    /// error and, importantly, never reaches password sign-in or token creation at all - so a
    /// disabled account cannot be distinguished from a wrong password (avoiding user enumeration)
    /// and never receives a valid session token.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ShouldRejectInactiveUser_WithoutPasswordSignInOrToken()
    {
        var (sut, db, _, signInManager, jwt, _) = Create();
        var user = new User("inactive", "HASH::pw", false);
        user.Deactivate();
        db.Users.Add(user);
        db.SaveChanges();

        var res = await sut.LoginAsync(new LoginCommand("inactive", "pw"), CancellationToken.None);

        Assert.False(res.Success);
        Assert.Contains("Invalid credentials", res.Error);
        signInManager.Verify(s => s.PasswordSignInAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        jwt.Verify(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), out It.Ref<DateTime>.IsAny, It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Verifies that after a lockout result, a subsequent login attempt with correct credentials
    /// can still succeed once the underlying lock has effectively expired, confirming lockouts are
    /// temporary rather than permanently barring the account.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ShouldSucceed_AfterIdentityLockExpired_AndValidCredentials()
    {
        var (sut, db, userManager, signInManager, jwt, _) = Create();
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

    /// <summary>
    /// Verifies that when a user's preferred language is null (meaning "Automatic", follow the
    /// browser), a login-time browser language hint does NOT get written to the user's stored
    /// preference - "Automatic" must remain automatic rather than being silently pinned to
    /// whatever the browser happened to send on a particular login.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ShouldNotOverwritePreferredLanguage_WhenAlreadyNull()
    {
        // Arrange: user has null PreferredLanguage (= "Automatic" mode)
        var (sut, db, _, signInManager, _, _) = Create();
        var user = new User("dave", "HASH::pw", false);
        // user.PreferredLanguage is null by default (= Automatic)
        db.Users.Add(user);
        db.SaveChanges();
        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        // Act: login sends browser language "en" as PreferredLanguage hint
        var result = await sut.LoginAsync(new LoginCommand("dave", "pw", null, "en", null), CancellationToken.None);

        // Assert: PreferredLanguage remains null so Automatic mode is preserved
        Assert.True(result.Success);
        var saved = db.Users.Single(u => u.UserName == "dave");
        Assert.Null(saved.PreferredLanguage);
    }

    /// <summary>
    /// Verifies that when a user has already chosen an explicit preferred language, a differing
    /// browser language hint sent during login does not overwrite that explicit choice - a user's
    /// deliberate language setting must take precedence over incidental browser locale data.
    /// </summary>
    [Fact]
    public async Task LoginAsync_ShouldNotOverwritePreferredLanguage_WhenExplicitLanguageIsSet()
    {
        // Arrange: user already has an explicit language preference "de"
        var (sut, db, _, signInManager, _, _) = Create();
        var user = new User("frank", "HASH::pw", false);
        user.SetPreferredLanguage("de");
        db.Users.Add(user);
        db.SaveChanges();
        signInManager.Setup(s => s.PasswordSignInAsync(user, "pw", false, true)).ReturnsAsync(SignInResult.Success);

        // Act: browser reports "en" during login
        var result = await sut.LoginAsync(new LoginCommand("frank", "pw", null, "en", null), CancellationToken.None);

        // Assert: explicit "de" must not be changed to "en"
        Assert.True(result.Success);
        var saved = db.Users.Single(u => u.UserName == "frank");
        Assert.Equal("de", saved.PreferredLanguage);
    }

}
