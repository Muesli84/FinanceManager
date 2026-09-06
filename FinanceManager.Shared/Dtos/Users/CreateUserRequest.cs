using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload for administrators to create a new user.
/// </summary>
/// <param name="Username">The username.</param>
/// <param name="Password">The password.</param>
/// <param name="IsAdmin">The is admin.</param>
/// <returns>The result.</returns>
public sealed record CreateUserRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    bool IsAdmin);
