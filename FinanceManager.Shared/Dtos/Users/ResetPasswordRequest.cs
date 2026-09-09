using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload to reset a user's password.
/// </summary>
/// <param name="NewPassword">The new password.</param>
/// <returns>The result.</returns>
public sealed record ResetPasswordRequest(
    [Required, MinLength(6)] string NewPassword);
