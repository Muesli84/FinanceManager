using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts; // added
using FinanceManager.Domain.Users;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Domain.Security; // new
using FinanceManager.Application.Security; // added

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UserAuthService> _logger;
    private readonly IIpBlockService _ipBlocks;
    private const int ThresholdAttempts = 3; // lock starting at 3rd failed attempt
    private static readonly TimeSpan ResetWindow = TimeSpan.FromDays(1); // keep escalation across typical lock durations
    private static readonly TimeSpan InitialLockDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxLockDuration = TimeSpan.FromHours(8);

    // Backwards-compatible ctor for existing tests/usages
    public UserAuthService(AppDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwt, IDateTimeProvider clock, ILogger<UserAuthService> logger)
        : this(db, passwordHasher, jwt, clock, logger, new NoopIpBlockService())
    { }

    public UserAuthService(AppDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwt, IDateTimeProvider clock, ILogger<UserAuthService> logger, IIpBlockService ipBlocks)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _clock = clock;
        _logger = logger;
        _ipBlocks = ipBlocks;
    }

    public async Task<Result<AuthResult>> RegisterAsync(RegisterUserCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Registering user {Username}", command.Username);

        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
        {
            _logger.LogWarning("Registration failed for {Username}: missing username or password", command.Username);
            return Result<AuthResult>.Fail("Username and password required");
        }

        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Username == command.Username, ct);
        if (exists)
        {
            _logger.LogWarning("Registration failed for {Username}: username already exists", command.Username);
            return Result<AuthResult>.Fail("Username already exists");
        }

        bool isFirst = !await _db.Users.AsNoTracking().AnyAsync(ct);
        var hash = _passwordHasher.Hash(command.Password);
        var user = new User(command.Username, hash, isFirst);
        if (!string.IsNullOrWhiteSpace(command.PreferredLanguage))
        {
            user.SetPreferredLanguage(command.PreferredLanguage);
        }
        if (!string.IsNullOrWhiteSpace(command.TimeZoneId))
        {
            user.SetTimeZoneId(command.TimeZoneId);
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Create self contact if not existing (first time user registration). This allows linking postings/other entities to the user as a contact.
        bool hasSelfContact = await _db.Contacts.AsNoTracking().AnyAsync(c => c.OwnerUserId == user.Id && c.Type == ContactType.Self, ct);
        if (!hasSelfContact)
        {
            _db.Contacts.Add(new Contact(user.Id, user.Username, ContactType.Self, null));
            await _db.SaveChangesAsync(ct);
        }

        var expires = _clock.UtcNow.AddMinutes(30);
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires, user.PreferredLanguage, user.TimeZoneId);

        await new DemoDataService(_db).CreateDemoDataForUserAsync(user.Id, ct);

        _logger.LogInformation("User {UserId} ({Username}) registered (IsAdmin={IsAdmin})", user.Id, user.Username, user.IsAdmin);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Login attempt for {Username} from {Ip}", command.Username, command.IpAddress ?? "?");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == command.Username, ct);
        if (user is null)
        {
            if (!string.IsNullOrWhiteSpace(command.IpAddress))
            {
                await _ipBlocks.RegisterUnknownUserFailureAsync(command.IpAddress!, ct);
            }
            _logger.LogWarning("Login failed: user {Username} not found (Ip={Ip})", command.Username, command.IpAddress);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > _clock.UtcNow)
        {
            _logger.LogWarning("Login blocked for {Username}: locked until {LockedUntil}", user.Username, user.LockedUntilUtc);
            return Result<AuthResult>.Fail("Account locked");
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            var failed = user.RegisterFailedLogin(_clock.UtcNow, ResetWindow);
            if (failed >= ThresholdAttempts)
            {
                // Escalating lock: 3rd -> 5m, 4th -> 10m, 5th -> 20m, ... capped at 8h
                var exponent = failed - ThresholdAttempts; // 0 for 3rd
                double minutes = InitialLockDuration.TotalMinutes * Math.Pow(2, Math.Max(0, exponent));
                var lockDuration = TimeSpan.FromMinutes(minutes);
                if (lockDuration > MaxLockDuration)
                {
                    lockDuration = MaxLockDuration;
                }
                user.SetLockedUntil(_clock.UtcNow.Add(lockDuration));

                if (!string.IsNullOrWhiteSpace(command.IpAddress))
                {
                    await _ipBlocks.BlockByAddressAsync(command.IpAddress!, "User failed login threshold reached", ct);
                }
            }
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning("Login failed (invalid credentials) for {Username} (Failed={Failed}, Ip={Ip})", user.Username, failed, command.IpAddress);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        // success ? reset counters
        user.MarkLogin(_clock.UtcNow);
        user.SetLockedUntil(null);

        // one-time capture of missing preferences from client
        if (string.IsNullOrWhiteSpace(user.PreferredLanguage) && !string.IsNullOrWhiteSpace(command.PreferredLanguage))
        {
            user.SetPreferredLanguage(command.PreferredLanguage);
        }
        if (string.IsNullOrWhiteSpace(user.TimeZoneId) && !string.IsNullOrWhiteSpace(command.TimeZoneId))
        {
            user.SetTimeZoneId(command.TimeZoneId);
        }

        await _db.SaveChangesAsync(ct);

        var expires = _clock.UtcNow.AddMinutes(30);
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires, user.PreferredLanguage, user.TimeZoneId);

        _logger.LogInformation("Login success for {UserId} ({Username}) from {Ip}", user.Id, user.Username, command.IpAddress);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    // Minimal no-op implementation to keep legacy constructors/tests working
    private sealed class NoopIpBlockService : IIpBlockService
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
