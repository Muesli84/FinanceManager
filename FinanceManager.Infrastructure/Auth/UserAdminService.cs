using FinanceManager.Application.Users;
using FinanceManager.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Auth;

public sealed class UserAdminService : IUserAdminService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public UserAdminService(AppDbContext db, IPasswordHasher passwordHasher)
    { _db = db; _passwordHasher = passwordHasher; }

    public async Task<IReadOnlyList<UserAdminDto>> ListAsync(CancellationToken ct)
    {
        return await _db.Users.AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserAdminDto(u.Id, u.Username, u.IsAdmin, u.Active, u.LockedUntilUtc, u.LastLoginUtc, u.PreferredLanguage))
            .ToListAsync(ct);
    }

    public async Task<UserAdminDto?> GetAsync(Guid id, CancellationToken ct)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserAdminDto(u.Id, u.Username, u.IsAdmin, u.Active, u.LockedUntilUtc, u.LastLoginUtc, u.PreferredLanguage))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<UserAdminDto> CreateAsync(string username, string password, bool isAdmin, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username required", nameof(username));
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password required", nameof(password));
        bool exists = await _db.Users.AnyAsync(u => u.Username == username, ct);
        if (exists) throw new InvalidOperationException("Username already exists");
        var user = new User(username, _passwordHasher.Hash(password), isAdmin);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return new UserAdminDto(user.Id, user.Username, user.IsAdmin, user.Active, user.LockedUntilUtc, user.LastLoginUtc, user.PreferredLanguage);
    }

    public async Task<UserAdminDto?> UpdateAsync(Guid id, string? username, bool? isAdmin, bool? active, string? preferredLanguage, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return null;
        if (!string.IsNullOrWhiteSpace(username) && !string.Equals(user.Username, username, StringComparison.OrdinalIgnoreCase))
        {
            bool exists = await _db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct);
            if (exists) throw new InvalidOperationException("Username already exists");
            user.GetType().GetProperty("Username")!.SetValue(user, username.Trim());
        }
        if (isAdmin.HasValue)
        {
            user.GetType().GetProperty("IsAdmin")!.SetValue(user, isAdmin.Value);
        }
        if (active.HasValue && user.Active != active.Value)
        {
            if (!active.Value)
            {
                user.Deactivate();
            }
            else
            {
                user.GetType().GetProperty("Active")!.SetValue(user, true);
            }
        }
        if (preferredLanguage != null)
        {
            user.SetPreferredLanguage(preferredLanguage);
        }
        await _db.SaveChangesAsync(ct);
        return new UserAdminDto(user.Id, user.Username, user.IsAdmin, user.Active, user.LockedUntilUtc, user.LastLoginUtc, user.PreferredLanguage);
    }

    public async Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newPassword)) throw new ArgumentException("Password required", nameof(newPassword));
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return false;
        user.GetType().GetProperty("PasswordHash")!.SetValue(user, _passwordHasher.Hash(newPassword));
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> UnlockAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return false;
        if (user.LockedUntilUtc != null)
        {
            user.SetLockedUntil(null);
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return false;
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
