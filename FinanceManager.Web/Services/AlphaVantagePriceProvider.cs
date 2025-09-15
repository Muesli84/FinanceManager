using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceManager.Web.Services;

public sealed class AlphaVantagePriceProvider : IPriceProvider
{
    private readonly AlphaVantage _api;

    public AlphaVantagePriceProvider(IConfiguration cfg)
    {
        var apiKey = cfg["AlphaVantage:ApiKey"] ?? cfg["ALPHAVANTAGE__APIKEY"] ?? throw new InvalidOperationException("AlphaVantage API key missing");
        _api = new AlphaVantage(new HttpClient { BaseAddress = new Uri("https://www.alphavantage.co/") }, apiKey);
    }

    public async Task<IReadOnlyList<(DateTime date, decimal close)>> GetDailyPricesAsync(string symbol, DateTime startDateExclusive, DateTime endDateInclusive, CancellationToken ct)
    {
        var series = await _api.GetTimeSeriesDailyAsync(symbol, ct);
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