using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record CreateUserRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    bool IsAdmin);

public sealed record UpdateUserRequest(
    [MinLength(3)] string? Username,
    bool? IsAdmin,
    bool? Active,
    string? PreferredLanguage);

public sealed record ResetPasswordRequest(
    [Required, MinLength(6)] string NewPassword);
