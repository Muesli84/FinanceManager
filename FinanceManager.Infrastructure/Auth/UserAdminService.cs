using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinanceManager.Domain.Contacts; // added
using FinanceManager.Domain;
using FinanceManager.Shared.Dtos; // added for ContactType
using Microsoft.AspNetCore.Identity;

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAdminService : IUserAdminService
{
    private readonly AppDbContext _db;
    private readonly UserManager<User> _userManager;
    private readonly IPasswordHashingService _passwordHasher;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(AppDbContext db, UserManager<User> userManager, IPasswordHashingService passwordHasher, ILogger<UserAdminService> logger)
    {
        _db = db;
        _userManager = userManager;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserAdminDto>> ListAsync(CancellationToken ct)
    {
        _logger.LogInformation("Listing users");
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.UserName)
            .ToListAsync(ct);

        var list = new List<UserAdminDto>(users.Count);
        foreach (var u in users)
        {
            var isAdmin = await _userManager.IsInRoleAsync(u, "Admin");
            list.Add(new UserAdminDto(
                u.Id,
                u.UserName,
                isAdmin,
                u.Active,
                u.LockoutEnd.HasValue ? u.LockoutEnd.Value.UtcDateTime : (DateTime?)null,
                u.LastLoginUtc,
                u.PreferredLanguage));
        }

        _logger.LogInformation("Listed {Count} users", list.Count);
        return list;
    }

    public async Task<UserAdminDto?> GetAsync(Guid id, CancellationToken ct)
    {
        _logger.LogInformation("Getting user {UserId}", id);
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .FirstOrDefaultAsync(ct);
        if (user == null)
        {
            _logger.LogWarning("User {UserId} not found", id);
            return null;
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        return new UserAdminDto(
            user.Id,
            user.UserName,
            isAdmin,
            user.Active,
            user.LockoutEnd.HasValue ? user.LockoutEnd.Value.UtcDateTime : (DateTime?)null,
            user.LastLoginUtc,
            user.PreferredLanguage);
    }

    public async Task<UserAdminDto> CreateAsync(string username, string password, bool isAdmin, CancellationToken ct)
    {
        _logger.LogInformation("Creating user {Username} (IsAdmin={IsAdmin})", username, isAdmin);
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username required", nameof(username));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password required", nameof(password));

        bool exists = await _db.Users.AnyAsync(u => u.UserName == username, ct);
        if (exists)
        {
            _logger.LogWarning("Cannot create user {Username}: already exists", username);
            throw new InvalidOperationException("Username already exists");
        }

        var user = new User(username, _passwordHasher.Hash(password), false);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Create self contact (admin path) — bypass public service restriction on creating Self contacts.
        bool hasSelf = await _db.Contacts.AsNoTracking().AnyAsync(c => c.OwnerUserId == user.Id && c.Type == ContactType.Self, ct);
        if (!hasSelf)
        {
            _db.Contacts.Add(new Contact(user.Id, user.UserName, ContactType.Self, null));
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Created self contact for user {UserId}", user.Id);
        }

        // Assign role if requested
        if (isAdmin)
        {
            try
            {
                var roleName = "Admin";
                // ensure role exists
                var roleExists = await _userManager.IsInRoleAsync(user, roleName);
                if (!roleExists)
                {
                    // Creating role requires RoleManager; try via UserManager.AddToRoleAsync which will fail if role missing.
                    var addRes = await _userManager.AddToRoleAsync(user, roleName);
                    if (!addRes.Succeeded)
                    {
                        _logger.LogWarning("Failed to add user {UserId} to role {Role}: {Errors}", user.Id, roleName, string.Join(';', addRes.Errors.Select(e => e.Description)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Assigning Admin role to created user failed (non-fatal)");
            }
        }

        _logger.LogInformation("Created user {UserId} ({Username})", user.Id, user.UserName);
        var finalIsAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        return new UserAdminDto(
            user.Id,
            user.UserName,
            finalIsAdmin,
            user.Active,
            user.LockoutEnd.HasValue ? user.LockoutEnd.Value.UtcDateTime : (DateTime?)null,
            user.LastLoginUtc,
            user.PreferredLanguage);
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

        if (!string.IsNullOrWhiteSpace(username) && !string.Equals(user.UserName, username, StringComparison.OrdinalIgnoreCase))
        {
            bool exists = await _db.Users.AnyAsync(u => u.UserName == username && u.Id != id, ct);
            if (exists)
            {
                _logger.LogWarning("Cannot rename user {UserId} to {NewUsername}: target already exists", id, username);
                throw new InvalidOperationException("Username already exists");
            }
            _logger.LogInformation("Renaming user {UserId} from {OldUsername} to {NewUsername}", id, user.UserName, username.Trim());
            user.Rename(username.Trim());
        }

        if (isAdmin.HasValue)
        {
            try
            {
                var roleName = "Admin";
                var currentlyInRole = await _userManager.IsInRoleAsync(user, roleName);
                if (isAdmin.Value && !currentlyInRole)
                {
                    var addRes = await _userManager.AddToRoleAsync(user, roleName);
                    if (!addRes.Succeeded)
                    {
                        _logger.LogWarning("Failed to add user {UserId} to role {Role}: {Errors}", id, roleName, string.Join(';', addRes.Errors.Select(e => e.Description)));
                    }
                }
                else if (!isAdmin.Value && currentlyInRole)
                {
                    var removeRes = await _userManager.RemoveFromRoleAsync(user, roleName);
                    if (!removeRes.Succeeded)
                    {
                        _logger.LogWarning("Failed to remove user {UserId} from role {Role}: {Errors}", id, roleName, string.Join(';', removeRes.Errors.Select(e => e.Description)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Updating roles for user {UserId} failed (non-fatal)", id);
            }
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
        var finalIsAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        return new UserAdminDto(
            user.Id,
            user.UserName,
            finalIsAdmin,
            user.Active,
            user.LockoutEnd.HasValue ? user.LockoutEnd.Value.UtcDateTime : (DateTime?)null,
            user.LastLoginUtc,
            user.PreferredLanguage);
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

        // Use Identity's UserManager to clear lockout and reset access-failed count.
        var setResult = await _userManager.SetLockoutEndDateAsync(user, null);
        if (!setResult.Succeeded)
        {
            _logger.LogWarning("Failed to clear lockout for user {UserId}: {Errors}", id, string.Join(';', setResult.Errors.Select(e => e.Description)));
            return false;
        }

        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);
        if (!resetResult.Succeeded)
        {
            _logger.LogWarning("Failed to reset access failed count for user {UserId}: {Errors}", id, string.Join(';', resetResult.Errors.Select(e => e.Description)));
            // lockout cleared but failed count not reset — still consider operation successful,
            // caller can retry/reset manually if needed.
        }

        _logger.LogInformation("Cleared lockout for user {UserId}", id);
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
