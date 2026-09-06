using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload for administrators to update an existing user.
/// </summary>
/// <param name="Username">The username.</param>
/// <param name="IsAdmin">The is admin.</param>
/// <param name="Active">The active.</param>
/// <param name="PreferredLanguage">The preferred language.</param>
/// <returns>The result.</returns>
public sealed record UpdateUserRequest(
    [MinLength(3)] string? Username,
    bool? IsAdmin,
    bool? Active,
    string? PreferredLanguage);
