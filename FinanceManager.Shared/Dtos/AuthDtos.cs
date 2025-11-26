using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record LoginRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    string? PreferredLanguage,
    string? TimeZoneId);

public sealed record RegisterRequest(
    [Required, MinLength(3)] string Username,
    [Required, MinLength(6)] string Password,
    string? PreferredLanguage,
    string? TimeZoneId);

// Match previous anonymous response shape: { user, isAdmin, exp }
public sealed record AuthOkResponse(string user, bool isAdmin, DateTime exp);
