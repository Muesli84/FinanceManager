using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload used to register the first user or when user creation is permitted.
/// </summary>
/// <param name="Username">Desired user name.</param>
/// <param name="Password">Desired password.</param>
/// <param name="PreferredLanguage">Optional preferred language code.</param>
/// <param name="TimeZoneId">Optional time zone identifier.</param>
public sealed record RegisterRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    string? PreferredLanguage,
    string? TimeZoneId);
