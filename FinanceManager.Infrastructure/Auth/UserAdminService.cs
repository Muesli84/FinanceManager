using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Domain.Contacts; // added
using FinanceManager.Domain; // added for ContactType

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAdminService : IUserAdminService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(AppDbContext db, IPasswordHasher passwordHasher, ILogger<UserAdminService> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserAdminDto>> ListAsync(CancellationToken ct)
    {
        _logger.LogInformation("Listing users");
        var list = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserAdminDto(u.Id, u.Username, u.IsAdmin, u.Active, u.LockedUntilUtc, u.LastLoginUtc, u.PreferredLanguage))
            .ToListAsync(ct);
        _logger.LogInformation("Listed {Count} users", list.Count);
        return list;
    }

    public async Task<UserAdminDto?> GetAsync(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Getting user {UserId}", id);
        var dto = await _db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserAdminDto(u.Id, u.Username, u.IsAdmin, u.Active, u.LockedUntilUtc, u.LastLoginUtc, u.PreferredLanguage))
            .FirstOrDefaultAsync(ct);
        if (dto == null)
        {
            _logger.LogWarning("User {UserId} not found", id);
        }
        return dto;
    }

    public async Task<UserAdminDto> CreateAsync(string username, string password, bool isAdmin, CancellationToken ct)
    {
        _logger.LogInformation("Creating user {Username} (IsAdmin={IsAdmin})", username, isAdmin);
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username required", nameof(username));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password required", nameof(password));

        bool exists = await _db.Users.AnyAsync(u => u.Username == username, ct);
        if (exists)
        {
            _logger.LogWarning("Cannot create user {Username}: already exists", username);
            throw new InvalidOperationException("Username already exists");
        }

        var user = new User(username, _passwordHasher.Hash(password), isAdmin);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Create self contact (admin path) – bypass public service restriction on creating Self contacts.
        bool hasSelf = await _db.Contacts.AsNoTracking().AnyAsync(c => c.OwnerUserId == user.Id && c.Type == ContactType.Self, ct);
        if (!hasSelf)
        {
            _db.Contacts.Add(new Contact(user.Id, user.Username, ContactType.Self, null));
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created self contact for user {UserId}", user.Id);
        }

        _logger.LogInformation("Created user {UserId} ({Username})", user.Id, user.Username);
        return new UserAdminDto(user.Id, user.Username, user.IsAdmin, user.Active, user.LockedUntilUtc, user.LastLoginUtc, user.PreferredLanguage);
    }

    public async Task<UserAdminDto?> UpdateAsync(Guid id, string? username, bool? isAdmin, bool? active, string? preferredLanguage, CancellationToken ct)
    {
        _logger.LogInformation("Updating user {UserId}", id);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for update", id);
            return null;
        }

        if (!string.IsNullOrWhiteSpace(username) && !string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            bool exists = await _db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct);
            if (exists)
            {
            _logger.LogWarning("Cannot rename user {UserId} to {NewUsername}: target already exists", id, username);
                throw new InvalidOperationException("Username already exists");
            }
            _logger.LogInformation("Renaming user {UserId} from {OldUsername} to {NewUsername}", id, user.Username, username.Trim());
            user.Rename(username.Trim());
        }

        if (isAdmin.HasValue && user.IsAdmin != isAdmin.Value)
        {
            _logger.LogInformation("Changing admin flag for {UserId} to {IsAdmin}", id, isAdmin.Value);
            user.SetAdmin(isAdmin.Value);
        }

        if (active.HasValue && user.Active != active.Value)
        {
            if (!active.Value)
            {
                _logger.LogInformation("Deactivating user {UserId}", id);
                user.Deactivate();
            }
            else
            {
                _logger.LogInformation("Activating user {UserId}", id);
                user.Activate();
            }
        }

        if (preferredLanguage != null && preferredLanguage != user.PreferredLanguage)
        {
            _logger.LogInformation("Setting preferred language for user {UserId} to {Lang}", id, preferredLanguage);
            user.SetPreferredLanguage(preferredLanguage);
        }

        await _db.SaveChangesAsync(ct);
        return new UserAdminDto(user.Id, user.Username, user.IsAdmin, user.Active, user.LockedUntilUtc, user.LastLoginUtc, user.PreferredLanguage);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct)
    {
        _logger.LogInformation("Resetting password for user {UserId}", id);
        if (string.IsNullOrWhiteSpace(newPassword)) throw new ArgumentException("Password required", nameof(newPassword));
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for password reset", id);
            return false;
        }
        user.SetPasswordHash(_passwordHasher.Hash(newPassword));
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Password reset for user {UserId} completed", id);
        return true;
    }

    public async Task<bool> UnlockAsync(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Unlocking user {UserId}", id);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for unlock", id);
            return false;
        }
        if (user.LockedUntilUtc != null)
        {
            _logger.LogInformation("User {UserId} lock cleared (was locked until {LockedUntil})", id, user.LockedUntilUtc);
            user.SetLockedUntil(null);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            _logger.LogDebug("User {UserId} not locked", id);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Deleting user {UserId}", id);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found for delete", id);
            return false;
        }
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Deleted user {UserId}", id);
        return true;
    }
}
