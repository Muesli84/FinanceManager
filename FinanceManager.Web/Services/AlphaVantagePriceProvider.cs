using System.Net.Http.Json;
using FinanceManager.Application;

namespace FinanceManager.Web.Services;

public sealed class AlphaVantagePriceProvider : IPriceProvider
{
    private readonly IAlphaVantageKeyResolver _keys;
    private readonly ICurrentUserService _current;

    public AlphaVantagePriceProvider(IAlphaVantageKeyResolver keys, ICurrentUserService current)
    {
        _keys = keys;
        _current = current;
    }

    public async Task<IReadOnlyList<(DateTime date, decimal close)>> GetDailyPricesAsync(string symbol, DateTime startDateExclusive, DateTime endDateInclusive, CancellationToken ct)
    {
        // Benutzer-Schlüssel bevorzugen; wenn kein Benutzerkontext vorhanden oder kein Key -> freigegebener Admin-Key
        var apiKey = _current.IsAuthenticated
            ? await _keys.GetForUserAsync(_current.UserId, ct) // beinhaltet bereits Fallback auf GetSharedAsync
            : await _keys.GetSharedAsync(ct);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("AlphaVantage API key not configured. Provide a user key or enable sharing for an admin key.");
        }

        var api = new AlphaVantage(new HttpClient { BaseAddress = new Uri("https://www.alphavantage.co/") }, apiKey);
        var series = await api.GetTimeSeriesDailyAsync(symbol, ct);
        if (series is null) return Array.Empty<(DateTime, decimal)>();

        var list = new List<(DateTime date, decimal close)>();
        foreach (var (date, _, _, _, close, _) in series.Enumerate())
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { continue; }
            if (date <= startDateExclusive.Date || date > endDateInclusive.Date) { continue; }
            list.Add((date, close));
        }
        return list.OrderBy(x => x.date).ToList();
    }
}