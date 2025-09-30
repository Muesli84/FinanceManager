namespace FinanceManager.Application.Notifications;

/// <summary>
/// Provides information about public holidays for a given date and region.
/// Implementations may call external APIs or use embedded calendars.
/// </summary>
public interface IHolidayProvider
{
    /// <summary>
    /// Returns true if the specified local date is a public holiday in the given region.
    /// </summary>
    /// <param name="dateLocal">The local calendar date (no time component required).</param>
    /// <param name="countryCode">ISO 3166-1 alpha-2 country code (e.g., "DE", "US"). Nullable ? no holidays.</param>
    /// <param name="subdivisionCode">Optional ISO 3166-2 subdivision/state code (e.g., "DE-BY"). Nullable ? country-level only.</param>
    bool IsPublicHoliday(DateTime dateLocal, string? countryCode, string? subdivisionCode);
}
