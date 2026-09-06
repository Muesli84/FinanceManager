using FinanceManager.Application.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceManager.Infrastructure.Notifications;

/// <summary>
/// Simple in-memory holiday provider with a small built-in set and extension points.
/// Supports country-level days. Subdivision code currently ignored unless explicitly added.
/// </summary>
public sealed class InMemoryHolidayProvider : IHolidayProvider
{
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Minimal seed of country-level public holidays. The dictionary key is an ISO country code
    /// (case-insensitive) and the value is a set of month/day tuples representing fixed-date holidays.
    /// </summary>
    /// <returns>The result.</returns>
    private static readonly Dictionary<string, HashSet<(int Month, int Day)>> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = new(new[] { (1, 1), (5, 1), (12, 25), (12, 26) }),
        ["US"] = new(new[] { (1, 1), (7, 4), (12, 25) }),
        ["GB"] = new(new[] { (1, 1), (12, 25), (12, 26) }),
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryHolidayProvider"/> class.
    /// </summary>
    /// <param name="cache">An <see cref="IMemoryCache"/> instance used to cache computed holiday sets per country/year.</param>
    public InMemoryHolidayProvider(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// Determines whether the specified local date is a public holiday for the given country (and optional subdivision).
    /// </summary>
    /// <param name="dateLocal">The local date to check.</param>
    /// <param name="countryCode">The ISO country code (e.g. "DE", "US"). When null or whitespace the method returns <c>false</c>.</param>
    /// <param name="subdivisionCode">Optional subdivision code (state/province). Currently ignored by the default in-memory provider.</param>
    /// <returns><c>true</c> when the date is considered a public holiday for the specified country; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The provider caches computed holiday sets per (country, year) for 12 hours. The current implementation only
    /// contains a minimal set of fixed-date holidays and does not implement moveable feasts or country-specific weekend rules.
    /// Extend or replace this provider with an external API (e.g. Nager.Date) for production scenarios.
    /// </remarks>
    public bool IsPublicHoliday(DateTime dateLocal, string? countryCode, string? subdivisionCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        var code = countryCode.ToUpperInvariant();
        // cache per (code, year)
        var year = dateLocal.Year;
        var key = $"holidays:{code}:{year}";
        var set = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            var hs = new HashSet<DateTime>();
            if (Defaults.TryGetValue(code, out var md))
            {
                foreach (var (m, d) in md)
                {
                    hs.Add(new DateTime(year, m, d));
                }
            }
            // Extend: move fixed dates landing on weekend to previous Friday / next Monday? (country-specific)
            return hs;
        })!;
        return set.Contains(dateLocal.Date);
    }
}
