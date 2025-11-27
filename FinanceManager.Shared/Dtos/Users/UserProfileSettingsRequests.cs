using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// Request payload to update user profile settings including language, time zone, and API key options.
/// </summary>
/// <param name="PreferredLanguage">Optional language preference (short code).</param>
/// <param name="TimeZoneId">Optional time zone identifier.</param>
/// <param name="AlphaVantageApiKey">Optional AlphaVantage API key to set or replace.</param>
/// <param name="ClearAlphaVantageApiKey">When true, clears the stored AlphaVantage API key.</param>
/// <param name="ShareAlphaVantageApiKey">When true, enables sharing of the admin API key (admin only).</param>
public sealed record UserProfileSettingsUpdateRequest(
    [property: MaxLength(10)] string? PreferredLanguage,
    [property: MaxLength(100)] string? TimeZoneId,
    [property: MaxLength(120)] string? AlphaVantageApiKey,
    bool? ClearAlphaVantageApiKey,
    bool? ShareAlphaVantageApiKey
);
