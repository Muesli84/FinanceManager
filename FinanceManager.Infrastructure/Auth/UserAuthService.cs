using FinanceManager.Application;
using FinanceManager.Application.Security; // added
using FinanceManager.Application.Users;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts; // added
using FinanceManager.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UserAuthService> _logger;
    private readonly IIpBlockService _ipBlocks;
    private readonly RoleManager<IdentityRole<Guid>>? _roleManager;

    // Backwards-compatible ctor for existing tests/usages
    public UserAuthService(AppDbContext db, UserManager<User> userManager, SignInManager<User> signInManager, IJwtTokenService jwt, IPasswordHashingService passwordHasher, IDateTimeProvider clock, ILogger<UserAuthService> logger)
        : this(db, userManager, signInManager, jwt, passwordHasher, clock, logger, new NoopIpBlockService(), null)
    { }

    public UserAuthService(
        AppDbContext db,
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IJwtTokenService jwt,
        IPasswordHashingService passwordHasher,
        IDateTimeProvider clock,
        ILogger<UserAuthService> logger,
        IIpBlockService ipBlocks,
        RoleManager<IdentityRole<Guid>>? roleManager = null)
    {
        _db = db;
        _userManager = userManager;
        _signInManager = signInManager;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
        _clock = clock;
        _logger = logger;
        _ipBlocks = ipBlocks;
        _roleManager = roleManager;
    }

    public async Task<Result<AuthResult>> RegisterAsync(RegisterUserCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Registering user {Username}", command.Username);

        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            _logger.LogWarning("Registration failed for {Username}: missing username or password", command.Username);
            return Result<AuthResult>.Fail("Username and password required");
        }

        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.UserName == command.Username, ct);
        if (exists)
        {
            _logger.LogWarning("Registration failed for {Username}: username already exists", command.Username);
            return Result<AuthResult>.Fail("Username already exists");
        }

        bool isFirst = !await _db.Users.AsNoTracking().AnyAsync(ct);
        var user = new User(command.Username, _passwordHasher.Hash(command.Password), isFirst);
        if (!string.IsNullOrWhiteSpace(command.PreferredLanguage))
        {
            user.SetPreferredLanguage(command.PreferredLanguage);
        }
        if (!string.IsNullOrWhiteSpace(command.TimeZoneId))
        {
            user.SetTimeZoneId(command.TimeZoneId);
        }

        var createResult = await _user_manager_create_wrapper(user, command.Password);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Registration failed for {Username}: Identity errors: {Errors}", command.Username, string.Join(';', createResult.Errors.Select(e => e.Description)));
            return Result<AuthResult>.Fail("Registration failed");
        }


        // Ensure user is persisted/tracked in AppDbContext in case UserManager is mocked or uses a different store
        if (user.Id == Guid.Empty)
        {
            user.Id = Guid.NewGuid();
        }

        var tracked = await _db.Users.AsNoTracking().AnyAsync(u => u.Id == user.Id, ct);
        if (!tracked)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // make sure any domain changes are applied to tracked entity
            var trackedUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == user.Id, ct);
            if (trackedUser == null)
            {
                _db.Users.Add(user);
                await _db.SaveChangesAsync(ct);
            }
        }

        // Assign AspNet Identity Role if first user
        if (isFirst)
        {
            try
            {
                if (_roleManager != null)
                {
                    var roleName = "Admin";
                    if (!await _roleManager.RoleExistsAsync(roleName))
                    {
                        await _roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName, NormalizedName = roleName.ToUpperInvariant() });
                    }
                }

                var addToRoleResult = await _userManager.AddToRoleAsync(user, "Admin");
                if (!addToRoleResult.Succeeded)
                {
                    _logger.LogWarning("Failed to add user {UserId} to role Admin: {Errors}", user.Id, string.Join(';', addToRoleResult.Errors.Select(e => e.Description)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Assigning Admin role to first user failed (non-fatal)");
            }
        }

        bool hasSelfContact = await _db.Contacts.AsNoTracking().AnyAsync(c => c.OwnerUserId == user.Id && c.Type == ContactType.Self, ct);
        if (!hasSelfContact)
        {
            _db.Contacts.Add(new Contact(user.Id, user.UserName, ContactType.Self, null));
            await _db.SaveChangesAsync(ct);
        }

        await new DemoDataService(_db).CreateDemoDataForUserAsync(user.Id, ct);

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var token = _jwt.CreateToken(user.Id, user.UserName, isAdmin, out var expires, user.PreferredLanguage, user.TimeZoneId);
        _logger.LogInformation("User {UserId} ({Username}) registered (IsAdmin={IsAdmin})", user.Id, user.UserName, isAdmin);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.UserName, isAdmin, token, expires));
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Login attempt for {Username} from {Ip}", command.Username, command.IpAddress ?? "*hidden*");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == command.Username, ct);
        if (user is null)
        {
            if (!string.IsNullOrWhiteSpace(command.IpAddress))
            {
                await _ipBlocks.RegisterUnknownUserFailureAsync(command.IpAddress!, ct);
            }
            _logger.LogWarning("Login failed: user {Username} not found (Ip={Ip})", command.Username, command.IpAddress);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        // Use Identity's SignInManager and enable Identity-managed lockout.
        var signInResult = await _signInManager.PasswordSignInAsync(user, command.Password, isPersistent: false, lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            _logger.LogWarning("Login blocked for {Username}: identity lockout", user.UserName);
            if (!string.IsNullOrWhiteSpace(command.IpAddress))
            {
                // optional: mirror previous behavior by blocking IP when identity lockout occurs
                await _ipBlocks.BlockByAddressAsync(command.IpAddress!, "User failed login threshold reached (Identity lockout)", ct);
            }
            return Result<AuthResult>.Fail("Account locked");
        }

        if (!signInResult.Succeeded)
        {
            _logger.LogWarning("Login failed (invalid credentials) for {Username} (Ip={Ip})", user.UserName, command.IpAddress);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        // success: update optional preferences and persist
        if (string.IsNullOrWhiteSpace(user.PreferredLanguage) && !string.IsNullOrWhiteSpace(command.PreferredLanguage))
        {
            user.SetPreferredLanguage(command.PreferredLanguage);
        }
        if (string.IsNullOrWhiteSpace(user.TimeZoneId) && !string.IsNullOrWhiteSpace(command.TimeZoneId))
        {
            user.SetTimeZoneId(command.TimeZoneId);
        }

        await _db.SaveChangesAsync(ct);

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var token = _jwt.CreateToken(user.Id, user.UserName, isAdmin, out var expires, user.PreferredLanguage, user.TimeZoneId);

        _logger.LogInformation("Login success for {UserId} ({Username}) from {Ip}", user.Id, user.UserName, command.IpAddress);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.UserName, isAdmin, token, expires));
    }

    // wrapper to keep calls test-friendly / readable
    private Task<IdentityResult> _user_manager_create_wrapper(User user, string password)
        => _userManager.CreateAsync(user, password);

    // Minimal no-op implementation to keep legacy constructors/tests working
    public sealed class NoopIpBlockService : IIpBlockService
    {
        public Task<bool> BlockAsync(Guid id, string? reason, CancellationToken ct) => Task.FromResult(true);
        public Task BlockByAddressAsync(string ipAddress, string? reason, CancellationToken ct) => Task.CompletedTask;
        public Task<IpBlockDto> CreateAsync(string ipAddress, string? reason, bool isBlocked, CancellationToken ct)
            => Task.FromResult(new IpBlockDto(Guid.Empty, ipAddress, isBlocked, DateTime.UtcNow, reason, 0, null, DateTime.UtcNow, null));
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<IpBlockDto>> ListAsync(bool? onlyBlocked, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IpBlockDto>>(Array.Empty<IpBlockDto>());
        public Task RegisterUnknownUserFailureAsync(string ipAddress, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ResetCountersAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> UnblockAsync(Guid id, CancellationToken ct) => Task.FromResult(true);
        public Task<IpBlockDto?> UpdateAsync(Guid id, string? reason, bool? isBlocked, CancellationToken ct)
            => Task.FromResult<IpBlockDto?>(null);
    }
}
