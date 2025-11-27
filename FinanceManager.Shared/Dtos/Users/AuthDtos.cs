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

/// <summary>
/// Response payload returned after successful authentication for anonymous callers.
/// Matches shape: { user, isAdmin, exp }.
/// </summary>
/// <param name="user">Authenticated user name.</param>
/// <param name="isAdmin">True when the user has administrative privileges.</param>
/// <param name="exp">Token expiry timestamp (UTC).</param>
public sealed record AuthOkResponse(string user, bool isAdmin, DateTime exp);
