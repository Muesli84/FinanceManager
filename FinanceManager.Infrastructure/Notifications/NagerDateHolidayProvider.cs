using FinanceManager.Application.Notifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace FinanceManager.Infrastructure.Notifications;

/// <summary>
/// Provider using the Nager.Date public holidays API (https://date.nager.at) to determine public holidays.
/// Caches fetched year maps in-memory for efficiency. Supports country-wide and regional (county) holidays.
/// </summary>
public sealed class NagerDateHolidayProvider : IHolidayProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NagerDateHolidayProvider> _logger;

    /// <summary>
    /// Local in-memory cache used to store holiday maps per country/year. Entries are cached for 12 hours.
    /// </summary>
    /// <returns>The result.</returns>
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    /// <summary>
    /// Initializes a new instance of the <see cref="NagerDateHolidayProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory to create <see cref="HttpClient"/> instances used to call the Nager.Date API.</param>
    /// <param name="logger">Logger instance for diagnostic messages and warnings.</param>
    public NagerDateHolidayProvider(IHttpClientFactory httpClientFactory, ILogger<NagerDateHolidayProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Internal record type used to deserialize the Nager.Date API response.
    /// </summary>
    /// <param name="date">Date string in ISO format (yyyy-MM-dd).</param>
    /// <param name="localName">Local name of the holiday.</param>
    /// <param name="name">English name of the holiday.</param>
    /// <param name="countryCode">Country code for which the holiday applies.</param>
    /// <param name="counties">Optional list of county/subdivision codes where the holiday applies; null/empty means country-wide.</param>
    /// <returns>The result.</returns>
    private sealed record NagerHoliday(string date, string localName, string name, string countryCode, string[]? counties);

    /// <summary>
    /// Determines whether the provided local date is a public holiday for the specified country and optional subdivision.
    /// </summary>
    /// <param name="dateLocal">Local date to check.</param>
    /// <param name="countryCode">ISO country code (e.g. "DE", "US"). When null or whitespace the method returns <c>false</c>.</param>
    /// <param name="subdivisionCode">Optional subdivision code (state/province/county). When null, only country-wide holidays are considered.</param>
    /// <returns><c>true</c> if the date is a public holiday for the given country/subdivision; otherwise <c>false</c>.</returns>
    public bool IsPublicHoliday(DateTime dateLocal, string? countryCode, string? subdivisionCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return false;
        }

        var year = dateLocal.Year;
        var code = countryCode.ToUpperInvariant();
        var key = $"nager:map:{code}:{year}";

        // Cache a map of Date -> counties (null or empty means country-wide)
        if (!_cache.TryGetValue<Dictionary<DateTime, string[]?>>(key, out var map))
        {
            map = LoadYearAsync(year, code).GetAwaiter().GetResult();
            _cache.Set(key, map, TimeSpan.FromHours(12));
        }

        if (!map!.TryGetValue(dateLocal.Date, out var counties))
        {
            return false;
        }

        // No subdivision configured: only treat country-wide holidays as valid
        if (string.IsNullOrWhiteSpace(subdivisionCode))
        {
            return counties == null || counties.Length == 0;
        }

        // Subdivision provided: holiday counts if it's country-wide OR explicitly includes the subdivision
        if (counties == null || counties.Length == 0)
        {
            return true; // country-wide
        }

        var sub = subdivisionCode.ToUpperInvariant();
        return counties.Any(c => string.Equals(c, sub, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Loads the holiday map for the given year and country from the Nager.Date API.
    /// The returned dictionary maps a <see cref="DateTime"/> (date) to an optional list of subdivision codes (null or empty = country-wide).
    /// </summary>
    /// <param name="year">Year to load holidays for.</param>
    /// <param name="countryCode">ISO country code.</param>
    /// <returns>A dictionary mapping dates to subdivision code arrays. Returns an empty dictionary on failure.</returns>
    /// <remarks>
    /// Exceptions from the HTTP client are caught and result in an empty map with a warning log entry.
    /// </remarks>
    private async Task<Dictionary<DateTime, string[]?>> LoadYearAsync(int year, string countryCode)
    {
        var client = _httpClientFactory.CreateClient();
        client.BaseAddress ??= new Uri("https://date.nager.at");
        try
        {
            var data = await client.GetFromJsonAsync<List<NagerHoliday>>($"/api/v3/PublicHolidays/{year}/{countryCode}");
            var map = new Dictionary<DateTime, string[]?>();
            if (data != null)
            {
                foreach (var h in data)
                {
                    if (!DateTime.TryParse(h.date, out var d))
                    {
                        continue;
                    }
                    var key = d.Date;
                    if (!map.TryGetValue(key, out var existing))
                    {
                        map[key] = h.counties;
                    }
                    else
                    {
                        // Merge county lists if duplicates exist for same date
                        if (existing == null || existing.Length == 0 || h.counties == null || h.counties.Length == 0)
                        {
                            map[key] = Array.Empty<string>(); // treat as country-wide
                        }
                        else
                        {
                            map[key] = existing.Union(h.counties, StringComparer.OrdinalIgnoreCase).Distinct().ToArray();
                        }
                    }
                }
            }
            return map;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NagerDate API failed for {Year}/{Country}. Falling back to empty map.", year, countryCode);
            return new Dictionary<DateTime, string[]?>();
        }
    }
}
