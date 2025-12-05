using FinanceManager.Application.Notifications;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace FinanceManager.Infrastructure.Notifications;

/// <summary>
/// Provider using https://date.nager.at API. Supports country-wide and regional (counties) holidays.
/// </summary>
public sealed class NagerDateHolidayProvider : IHolidayProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NagerDateHolidayProvider> _logger;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public NagerDateHolidayProvider(IHttpClientFactory httpClientFactory, ILogger<NagerDateHolidayProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private sealed record NagerHoliday(string date, string localName, string name, string countryCode, string[]? counties);

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
