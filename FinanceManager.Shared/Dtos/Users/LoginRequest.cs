using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload used to authenticate a user and obtain a token.
/// </summary>
/// <param name="Username">Login user name.</param>
/// <param name="Password">Login password.</param>
/// <param name="PreferredLanguage">Optional preferred language code.</param>
/// <param name="TimeZoneId">Optional time zone identifier.</param>
public sealed record LoginRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    string? PreferredLanguage,
    string? TimeZoneId);
