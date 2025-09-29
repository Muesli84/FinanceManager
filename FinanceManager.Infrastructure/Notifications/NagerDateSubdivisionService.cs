using System.Net.Http; // IHttpClientFactory
using System.Net.Http.Json;
using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceManager.Infrastructure.Notifications;

public sealed class NagerDateSubdivisionService : IHolidaySubdivisionService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    public NagerDateSubdivisionService(IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    private sealed record NagerHoliday(string date, string localName, string name, string countryCode, string[]? counties);

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
