using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain;
using FinanceManager.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Domain.Contacts; // added

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<UserAuthService> _logger;
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan BaseLockDuration = TimeSpan.FromMinutes(5); // first lock

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
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires);

        _logger.LogInformation("User {UserId} ({Username}) registered (IsAdmin={IsAdmin})", user.Id, user.Username, user.IsAdmin);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        _logger.LogInformation("Login attempt for {Username}", command.Username);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == command.Username, ct);
        if (user is null)
        {
            _logger.LogWarning("Login failed: user {Username} not found", command.Username);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > _clock.UtcNow)
        {
            _logger.LogWarning("Login blocked for {Username}: locked until {LockedUntil}", user.Username, user.LockedUntilUtc);
            return Result<AuthResult>.Fail("Account locked");
        }

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            await RegisterFailedAttemptAsync(user, ct);
            _logger.LogWarning("Login failed (invalid credentials) for {Username}", user.Username);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        user.MarkLogin(_clock.UtcNow);
        user.SetLockedUntil(null);
        await _db.SaveChangesAsync(ct);

        var expires = _clock.UtcNow.AddMinutes(30);
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires);

        _logger.LogInformation("Login success for {UserId} ({Username})", user.Id, user.Username);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    private async Task RegisterFailedAttemptAsync(User user, CancellationToken ct)
    {
        var failed = user.IncrementFailedLoginAttempts();
        if (failed >= MaxFailedAttempts)
        {
            int escalation = failed - MaxFailedAttempts; // 0 first lock window
            var duration = TimeSpan.FromTicks(BaseLockDuration.Ticks * (long)Math.Pow(2, escalation));
            if (duration > TimeSpan.FromHours(8))
            {
                duration = TimeSpan.FromHours(8);
            }
            var until = _clock.UtcNow.Add(duration);
            user.SetLockedUntil(until);
            _logger.LogWarning("User {UserId} ({Username}) locked after {Failed} failed attempts until {LockedUntil} (DurationMinutes={Duration})",
                user.Id, user.Username, failed, until, duration.TotalMinutes);
        }
        else
        {
            _logger.LogInformation("Failed login attempt {Failed} for {Username}", failed, user.Username);
        }

        await _db.SaveChangesAsync(ct);
    }
}
