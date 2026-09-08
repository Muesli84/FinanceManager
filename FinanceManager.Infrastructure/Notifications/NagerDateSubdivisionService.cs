using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Json;

namespace FinanceManager.Infrastructure.Notifications;

/// <summary>
/// Service that resolves available holiday subdivisions (counties/regions) for providers that support them.
/// This implementation queries the Nager.Date API and caches results in memory per country/year.
/// </summary>
public sealed class NagerDateSubdivisionService : IHolidaySubdivisionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="NagerDateSubdivisionService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory used to create <see cref="HttpClient"/> instances for API calls.</param>
    /// <param name="cache">Memory cache used to store subdivision lists per country/year.</param>
    public NagerDateSubdivisionService(IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    /// <summary>
    /// Internal record shape matching the Nager.Date PublicHolidays API response entries.
    /// </summary>
    /// <param name="date">ISO date string (yyyy-MM-dd).</param>
    /// <param name="localName">Local name of the holiday.</param>
    /// <param name="name">English name of the holiday.</param>
    /// <param name="countryCode">ISO country code the holiday applies to.</param>
    /// <param name="counties">Optional array of subdivision codes where the holiday applies; null/empty means country-wide.</param>
    /// <returns>The result.</returns>
    private sealed record NagerHoliday(string date, string localName, string name, string countryCode, string[]? counties);

    /// <summary>
    /// Retrieves subdivision codes for the given provider and country.
    /// </summary>
    /// <param name="provider">The holiday provider kind to query. Only <see cref="HolidayProviderKind.NagerDate"/> is supported by this implementation.</param>
    /// <param name="countryCode">The ISO country code (e.g. "DE", "US"). When null or whitespace an empty array is returned.</param>
    /// <param name="ct">Cancellation token to cancel the HTTP request / operation.</param>
    /// <returns>
    /// An array of subdivision codes (case-preserving) available for the specified country. Returns an empty array when none are found
    /// or when the provider is unsupported or an error occurs while fetching data.
    /// </returns>
    /// <remarks>
    /// Results are cached per (country, current year) for 12 hours to reduce API calls. The method swallows downstream errors and
    /// returns an empty array in failure scenarios to avoid bubbling transient HTTP issues to callers.
    /// </remarks>
    public async Task<string[]> GetSubdivisionsAsync(HolidayProviderKind provider, string countryCode, CancellationToken ct)
    {
        if (provider != HolidayProviderKind.NagerDate)
        {
            return Array.Empty<string>();
        }
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return Array.Empty<string>();
        }

        var code = countryCode.ToUpperInvariant();
        var year = DateTime.UtcNow.Year;
        var cacheKey = $"nager:subdivisions:{code}:{year}";
        if (_cache.TryGetValue<string[]>(cacheKey, out var cached))
        {
            return cached!;
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress ??= new Uri("https://date.nager.at");

        try
        {
            var holidays = await client.GetFromJsonAsync<List<NagerHoliday>>($"/api/v3/PublicHolidays/{year}/{code}", ct);
            if (holidays is null || holidays.Count == 0)
            {
                return Array.Empty<string>();
            }

            var result = holidays
                .Where(h => h.counties != null && h.counties.Length > 0)
                .SelectMany(h => h.counties!)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _cache.Set(cacheKey, result, TimeSpan.FromHours(12));
            return result;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
