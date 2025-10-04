namespace FinanceManager.Shared.Dtos;

public sealed class UserProfileSettingsDto
{
    public string? PreferredLanguage { get; set; }
    public string? TimeZoneId { get; set; }

    // New: AlphaVantage key flags
    public bool HasAlphaVantageApiKey { get; set; }
    public bool ShareAlphaVantageApiKey { get; set; }
}
