using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts; // added
using FinanceManager.Domain.Users;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Domain.Security; // new

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UserAuthService> _logger;
    private const int ThresholdAttempts = 3; // Sperre ab 3
    private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(5);

    public UserAuthService(AppDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwt, IDateTimeProvider clock, ILogger<UserAuthService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _clock = clock;
        _logger = logger;
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
            await RegisterUnknownUserFailureAsync(command.IpAddress, ct);
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
                await BlockIpIfNeededAsync(command.IpAddress, "User failed login threshold reached", ct);
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

    private async Task RegisterUnknownUserFailureAsync(string? ip, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip)) { return; }
        var now = _clock.UtcNow;
        var block = await _db.IpBlocks.FirstOrDefaultAsync(b => b.IpAddress == ip, ct);
        if (block == null)
        {
            block = new IpBlock(ip);
            _db.IpBlocks.Add(block);
        }
        var count = block.RegisterUnknownUserFailure(now, ResetWindow);
        if (count >= ThresholdAttempts)
        {
            block.Block(now, "Unknown user failures threshold reached");
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task BlockIpIfNeededAsync(string? ip, string reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ip)) { return; }
        var block = await _db.IpBlocks.FirstOrDefaultAsync(b => b.IpAddress == ip, ct);
        if (block == null)
        {
            block = new IpBlock(ip);
            _db.IpBlocks.Add(block);
        }
        // escalate to block (user-side failures already thresholded in caller)
        block.Block(_clock.UtcNow, reason);
        await _db.SaveChangesAsync(ct);
    }
}
