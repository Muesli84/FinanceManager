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

    // Minimal seed: New Year's Day, Labour Day, Christmas (country-level examples)
    // For production, extend via configuration or external API (Nager.Date) wrapper.
    private static readonly Dictionary<string, HashSet<(int Month,int Day)>> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = new(new[] { (1,1), (5,1), (12,25), (12,26) }),
        ["US"] = new(new[] { (1,1), (7,4), (12,25) }),
        ["GB"] = new(new[] { (1,1), (12,25), (12,26) }),
    };

    public InMemoryHolidayProvider(IMemoryCache cache)
    {
        _cache = cache;
    }

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
                foreach (var (m,d) in md)
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
