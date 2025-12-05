using FinanceManager.Application;
using System.Net;

namespace FinanceManager.Web.Services;

public sealed class AlphaVantagePriceProvider : IPriceProvider
{
    private readonly IAlphaVantageKeyResolver _keys;
    private readonly ICurrentUserService _current;
    private readonly IHttpClientFactory _httpFactory;

    public AlphaVantagePriceProvider(IAlphaVantageKeyResolver keys, ICurrentUserService current, IHttpClientFactory httpFactory)
    {
        _keys = keys;
        _current = current;
        _httpFactory = httpFactory;
    }

    public async Task<IReadOnlyList<(DateTime date, decimal close)>> GetDailyPricesAsync(string symbol, DateTime startDateExclusive, DateTime endDateInclusive, CancellationToken ct)
    {
        // Benutzer-Schlüssel bevorzugen; Fallback auf freigegebenen Admin-Schlüssel
        var apiKey = _current.IsAuthenticated
            ? await _keys.GetForUserAsync(_current.UserId, ct)
            : await _keys.GetSharedAsync(ct);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("AlphaVantage API key not configured. Provide a user key or enable sharing for an admin key.");
        }

        var http = _httpFactory.CreateClient("AlphaVantage");
        var api = new AlphaVantage(http, apiKey);

        // Resilienz: Retry bei transienten HTTP-Fehlern, kein Retry bei Rate-Limit (RequestLimitExceededException) / 429
        var series = await ExecuteWithRetryAsync(
            async () => await api.GetTimeSeriesDailyAsync(symbol, ct),
            maxRetries: 3,
            initialDelayMs: 400,
            ct);

        if (series is null) { return Array.Empty<(DateTime, decimal)>(); }

        var list = new List<(DateTime date, decimal close)>();
        foreach (var (date, _, _, _, close, _) in series.Enumerate())
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) { continue; }
            if (date <= startDateExclusive.Date || date > endDateInclusive.Date) { continue; }
            list.Add((date, close));
        }
        return list.OrderBy(x => x.date).ToList();
    }

    private static async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T?>> operation, int maxRetries, int initialDelayMs, CancellationToken ct)
    {
        int attempt = 0;
        var delayMs = initialDelayMs;

        while (true)
        {
            try
            {
                return await operation();
            }
            catch (RequestLimitExceededException)
            {
                // AlphaVantage eigenes Rate-Limit-Signal -> kein Retry, sofort weiterwerfen
                throw;
            }
            catch (HttpRequestException ex) when (IsTransient(ex))
            {
                attempt++;
                if (attempt > maxRetries) { throw; }
                var jitter = Random.Shared.Next(0, 150);
                await Task.Delay(delayMs + jitter, ct);
                delayMs *= 2;
                continue;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout (kein explizites Cancel) -> als transient behandeln
                attempt++;
                if (attempt > maxRetries) { throw; }
                var jitter = Random.Shared.Next(0, 150);
                await Task.Delay(delayMs + jitter, ct);
                delayMs *= 2;
                continue;
            }
        }
    }

    private static bool IsTransient(HttpRequestException ex)
    {
        // Retry bei 408/5xx/Bad Gateway/Service Unavailable/Gateway Timeout; kein Retry bei 429
        if (ex.StatusCode is null) { return true; }
        return ex.StatusCode switch
        {
            HttpStatusCode.RequestTimeout => true,       // 408
            HttpStatusCode.InternalServerError => true,  // 500
            HttpStatusCode.BadGateway => true,           // 502
            HttpStatusCode.ServiceUnavailable => true,   // 503
            HttpStatusCode.GatewayTimeout => true,       // 504
            HttpStatusCode.TooManyRequests => false,     // 429 -> nicht hier retryn, AlphaVantage liefert meist 200+Note
            _ => (int)ex.StatusCode.Value >= 500         // sonstige 5xx
        };
    }
}