using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record UserProfileSettingsUpdateRequest(
    [property: MaxLength(10)] string? PreferredLanguage,
    [property: MaxLength(100)] string? TimeZoneId,
    [property: MaxLength(120)] string? AlphaVantageApiKey,
    bool? ClearAlphaVantageApiKey,
    bool? ShareAlphaVantageApiKey
);
