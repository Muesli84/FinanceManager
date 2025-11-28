using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload for administrators to create a new user.
/// </summary>
public sealed record CreateUserRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    bool IsAdmin);
