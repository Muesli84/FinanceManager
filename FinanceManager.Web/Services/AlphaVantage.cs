using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceManager.Web.Services;

public sealed class AlphaVantage
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private DateTime _skipRequestsUntilUtc = DateTime.MinValue;

    public AlphaVantage(HttpClient http, string apiKey)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://www.alphavantage.co/");
        }
    }

    public bool LimitExceeded => DateTime.UtcNow < _skipRequestsUntilUtc;

    public async Task<TimeSeriesDaily?> GetTimeSeriesDailyAsync(string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) { throw new ArgumentException("API key required", nameof(_apiKey)); }
        if (string.IsNullOrWhiteSpace(symbol)) { throw new ArgumentException("symbol required", nameof(symbol)); }
        if (LimitExceeded)
        {
            throw new RequestLimitExceededException($"AlphaVantage limit exceeded. Next attempt after {_skipRequestsUntilUtc:u}.");
        }

        var url = $"query?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(symbol)}&outputsize=full&apikey={_apiKey}";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (root.TryGetProperty("Note", out var noteEl))
        {
            var note = noteEl.GetString() ?? "Rate limit reached";
            // Conservative: block until start of next UTC day
            _skipRequestsUntilUtc = DateTime.UtcNow.Date.AddDays(1);
            throw new RequestLimitExceededException(note);
        }
        if (root.TryGetProperty("Information", out var infoEl))
        {
            var info = infoEl.GetString() ?? "Request information";
            _skipRequestsUntilUtc = DateTime.UtcNow.Date.AddDays(1);
            throw new RequestLimitExceededException(info);
        }
        if (root.TryGetProperty("Error Message", out var errEl))
        {
            // Invalid symbol or other error
            var msg = errEl.GetString() ?? "Unknown AlphaVantage error";
            throw new InvalidOperationException(msg);
        }

        // Rewind by re-serializing to our model
        var json = JsonSerializer.Deserialize<TimeSeriesDailyResponse>(root.GetRawText(), JsonOptions);
        if (json is null || json.TimeSeries is null)
        {
            return null;
        }
        return new TimeSeriesDaily(json.TimeSeries);
    }

    public sealed class TimeSeriesDaily
    {
        public IReadOnlyDictionary<string, BarRaw> Raw { get; }
        public TimeSeriesDaily(Dictionary<string, BarRaw> raw) { Raw = raw; }

        public IEnumerable<(DateTime date, decimal open, decimal high, decimal low, decimal close, long volume)> Enumerate()
        {
            foreach (var kv in Raw)
            {
                if (!DateTime.TryParse(kv.Key, out var d)) { continue; }
                var b = kv.Value;
                if (!TryParseDecimal(b.Open, out var open) || !TryParseDecimal(b.High, out var high) || !TryParseDecimal(b.Low, out var low) || !TryParseDecimal(b.Close, out var close) || !long.TryParse(b.Volume, out var vol))
                {
                    continue;
                }
                yield return (d.Date, open, high, low, close, vol);
            }
        }

        private static bool TryParseDecimal(string? s, out decimal value)
            => decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    public sealed class TimeSeriesDailyResponse
    {
        [JsonPropertyName("Time Series (Daily)")]
        public Dictionary<string, BarRaw>? TimeSeries { get; set; }

        // Throttle / error notes
        [JsonPropertyName("Note")] public string? Note { get; set; }
        [JsonPropertyName("Information")] public string? Information { get; set; }
        [JsonPropertyName("Error Message")] public string? Error { get; set; }
    }

    public sealed class BarRaw
    {
        [JsonPropertyName("1. open")] public string? Open { get; set; }
        [JsonPropertyName("2. high")] public string? High { get; set; }
        [JsonPropertyName("3. low")] public string? Low { get; set; }
        [JsonPropertyName("4. close")] public string? Close { get; set; }
        [JsonPropertyName("5. volume")] public string? Volume { get; set; }
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed class RequestLimitExceededException : ApplicationException
{
    public RequestLimitExceededException(string message) : base(message) { }
}