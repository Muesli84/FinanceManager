namespace FinanceManager.Web.Services.Help;

/// <summary>
/// Defines the help languages supported by runtime localization and build-time help assets.
/// </summary>
public static partial class HelpLanguages
{
    /// <summary>
    /// The default help language.
    /// </summary>
    public const string DefaultLanguage = GeneratedDefaultLanguage;

    /// <summary>
    /// All supported help languages.
    /// </summary>
    /// <returns>The result.</returns>
    public static readonly IReadOnlyList<string> Supported = SupportedLanguageCodes
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Normalizes and validates a help language code.
    /// </summary>
    /// <param name="language">The input language code.</param>
    /// <param name="normalizedLanguage">The normalized language code.</param>
    /// <returns><c>true</c> when the language is supported.</returns>
    public static bool TryNormalize(string? language, out string normalizedLanguage)
    {
        normalizedLanguage = (language ?? string.Empty).Trim().ToLowerInvariant();
        return Supported.Contains(normalizedLanguage, StringComparer.Ordinal);
    }
}
