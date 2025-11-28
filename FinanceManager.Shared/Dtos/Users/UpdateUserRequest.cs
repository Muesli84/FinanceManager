using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload for administrators to update an existing user.
/// </summary>
public sealed record UpdateUserRequest(
    [MinLength(3)] string? Username,
    bool? IsAdmin,
    bool? Active,
    string? PreferredLanguage);
