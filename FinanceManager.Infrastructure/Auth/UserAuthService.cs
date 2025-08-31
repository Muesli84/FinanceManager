using FinanceManager.Application;
using FinanceManager.Application.Users;
using FinanceManager.Domain;
using FinanceManager.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAuthService : IUserAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IDateTimeProvider _clock;
    private const int MaxFailedAttempts = 3; // TODO implement tracking
    private readonly TimeSpan LockDuration = TimeSpan.FromMinutes(60);

    public UserAuthService(AppDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwt, IDateTimeProvider clock)
    { _db = db; _passwordHasher = passwordHasher; _jwt = jwt; _clock = clock; }

    public async Task<Result<AuthResult>> RegisterAsync(RegisterUserCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Username) || string.IsNullOrWhiteSpace(command.Password))
            return Result<AuthResult>.Fail("Username and password required");

        var exists = await _db.Users.AsNoTracking().AnyAsync(u => u.Username == command.Username, ct);
        if (exists) return Result<AuthResult>.Fail("Username already exists");

        bool isFirst = !await _db.Users.AsNoTracking().AnyAsync(ct);
        var hash = _passwordHasher.Hash(command.Password);
        var user = new User(command.Username, hash, isFirst);
        if (!string.IsNullOrWhiteSpace(command.PreferredLanguage))
            user.SetPreferredLanguage(command.PreferredLanguage);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        var expires = _clock.UtcNow.AddMinutes(30);
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    public async Task<Result<AuthResult>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == command.Username, ct);
        if (user is null) return Result<AuthResult>.Fail("Invalid credentials");
        if (user.LockedUntilUtc.HasValue && user.LockedUntilUtc.Value > _clock.UtcNow)
            return Result<AuthResult>.Fail("Account locked");

        if (!_passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            await RegisterFailedAttemptAsync(user, ct);
            return Result<AuthResult>.Fail("Invalid credentials");
        }

        user.MarkLogin(_clock.UtcNow);
        user.SetLockedUntil(null);
        await _db.SaveChangesAsync(ct);
        var expires = _clock.UtcNow.AddMinutes(30);
        var token = _jwt.CreateToken(user.Id, user.Username, user.IsAdmin, expires);
        return Result<AuthResult>.Ok(new AuthResult(user.Id, user.Username, user.IsAdmin, token, expires));
    }

    private async Task RegisterFailedAttemptAsync(User user, CancellationToken ct)
    {
        user.SetLockedUntil(_clock.UtcNow.Add(LockDuration));
        await _db.SaveChangesAsync(ct);
    }
}
