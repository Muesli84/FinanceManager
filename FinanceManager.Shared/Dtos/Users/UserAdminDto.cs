namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// DTO representing a user in the admin management context.
/// </summary>
/// <param name="Id">Unique identifier of the user.</param>
/// <param name="Username">Login name of the user.</param>
/// <param name="IsAdmin">True when the user has administrative privileges.</param>
/// <param name="Active">True when the user is active and allowed to sign in.</param>
/// <param name="LockoutEnd">UTC timestamp until which user is locked out (null if not locked).</param>
/// <param name="LastLoginUtc">UTC timestamp of the last successful login.</param>
/// <param name="PreferredLanguage">Optional ISO language code preferred by the user.</param>
public sealed record UserAdminDto(Guid Id, string Username, bool IsAdmin, bool Active, DateTime? LockoutEnd, DateTime LastLoginUtc, string? PreferredLanguage);
