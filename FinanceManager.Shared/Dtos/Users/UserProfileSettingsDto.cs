namespace FinanceManager.Shared.Dtos.Users;

/// <summary>
/// DTO representing a user's profile settings.
/// </summary>
public sealed class UserProfileSettingsDto
{
    /// <summary>Optional language preference code.</summary>
    public string? PreferredLanguage { get; set; }
    /// <summary>Optional time zone identifier.</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>True when the user has configured an AlphaVantage API key.</summary>
    public bool HasAlphaVantageApiKey { get; set; }
    /// <summary>True when the user is allowed to share the admin API key.</summary>
    public bool ShareAlphaVantageApiKey { get; set; }
    /// <summary>True when home page KPI data should be cached in the browser's local storage.</summary>
    public bool CacheKpisInLocalStorage { get; set; }

    /// <summary>True when confirmation dialogs should be shown before destructive or irreversible actions.</summary>
    public bool ShowConfirmations { get; set; } = true;
}
