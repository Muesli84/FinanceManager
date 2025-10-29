namespace FinanceManager.Application.Users;

public interface IUserAdminService
{
    Task<IReadOnlyList<UserAdminDto>> ListAsync(CancellationToken ct);
    Task<UserAdminDto?> GetAsync(Guid id, CancellationToken ct);
    Task<UserAdminDto> CreateAsync(string username, string password, bool isAdmin, CancellationToken ct);
    Task<UserAdminDto?> UpdateAsync(Guid id, string? username, bool? isAdmin, bool? active, string? preferredLanguage, CancellationToken ct);
    Task<bool> ResetPasswordAsync(Guid id, string newPassword, CancellationToken ct);
    Task<bool> UnlockAsync(Guid id, CancellationToken ct); // clears lock
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
}

public sealed record UserAdminDto(Guid Id, string Username, bool IsAdmin, bool Active, DateTime? LockoutEnd, DateTime LastLoginUtc, string? PreferredLanguage);
