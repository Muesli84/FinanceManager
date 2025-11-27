using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload for administrators to create a new user.
/// </summary>
public sealed record CreateUserRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    bool IsAdmin);

/// <summary>
/// Request payload for administrators to update an existing user.
/// </summary>
public sealed record UpdateUserRequest(
    [MinLength(3)] string? Username,
    bool? IsAdmin,
    bool? Active,
    string? PreferredLanguage);

/// <summary>
/// Request payload to reset a user's password.
/// </summary>
public sealed record ResetPasswordRequest(
    [Required, MinLength(6)] string NewPassword);
