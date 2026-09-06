using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceManager.Web.Services;

/// <summary>
/// Client service for interacting with the AlphaVantage quote/time-series API.
/// Encapsulates request throttling handling and returns strongly-typed time series data.
/// </summary>
public sealed class AlphaVantage
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private DateTime _skipRequestsUntilUtc = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of <see cref="AlphaVantage"/>.
    /// </summary>
    /// <param name="http">An <see cref="HttpClient"/> used to perform requests. Must not be <c>null</c>.</param>
    /// <param name="apiKey">AlphaVantage API key. When empty or null some operations will throw <see cref="ArgumentException"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is <c>null</c>.</exception>
    public AlphaVantage(HttpClient http, string apiKey)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey;
        if (_http.BaseAddress is null)
        {
            _http.BaseAddress = new Uri("https://www.alphavantage.co/");
        }
    }

    /// <summary>
    /// Gets a value indicating whether the client is currently blocked from issuing further requests
    /// because a rate-limit notice was previously observed. When <c>true</c> calls should be avoided
    /// until the configured next retry time.<br/>
    /// This value is set conservatively to the start of the next UTC day when rate-limit messages are received.
    /// </summary>
    public bool LimitExceeded => DateTime.UtcNow < _skipRequestsUntilUtc;

    /// <summary>
    /// Retrieves the full daily time series for the specified symbol.
    /// </summary>
    /// <param name="symbol">The symbol (ticker) to request, e.g. "MSFT". Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token used to cancel the HTTP request.</param>
    /// <returns>
    /// A <see cref="TimeSeriesDaily"/> instance containing raw bars when the symbol was found; otherwise <c>null</c>
    /// if the response did not contain a valid time series.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the configured API key or <paramref name="symbol"/> is missing/invalid.</exception>
    /// <exception cref="RequestLimitExceededException">Thrown when AlphaVantage returned a rate-limit notice and requests are blocked temporarily.</exception>
    /// <exception cref="InvalidOperationException">Thrown when AlphaVantage reports an error message for the request (for example invalid symbol).</exception>
    /// <exception cref="HttpRequestException">Thrown when the underlying HTTP request failed.</exception>
    /// <exception cref="JsonException">Thrown when the JSON response cannot be parsed into the expected model.</exception>
    public async Task<TimeSeriesDaily?> GetTimeSeriesDailyAsync(string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey)) { throw new ArgumentException("API key required", nameof(_apiKey)); }
        if (string.IsNullOrWhiteSpace(symbol.Replace("-", ""))) { throw new ArgumentException("symbol required", nameof(symbol)); }
        if (LimitExceeded)
        {
            throw new RequestLimitExceededException($"AlphaVantage limit exceeded. Next attempt after {_skipRequestsUntilUtc:u}.");
        }

        var url = $"query?function=TIME_SERIES_DAILY&symbol={Uri.EscapeDataString(symbol)}&outputsize=compact&apikey={_apiKey}";
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

    /// <summary>
    /// Container for the parsed daily time series returned by AlphaVantage.
    /// </summary>
    public sealed class TimeSeriesDaily
    {
        /// <summary>
        /// Raw dictionary mapping date string to numeric bar values.
        /// </summary>
        public IReadOnlyDictionary<string, BarRaw> Raw { get; }

        /// <summary>
        /// Initializes a new instance of <see cref="TimeSeriesDaily"/>.
        /// </summary>
        /// <param name="raw">Dictionary of raw bar values keyed by date string.</param>
        public TimeSeriesDaily(Dictionary<string, BarRaw> raw) { Raw = raw; }

        /// <summary>
        /// Enumerates parsed bars as tuples with strongly typed values.
        /// </summary>
        /// <returns>An enumeration of tuples containing the date and open/high/low/close/volume values.</returns>
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

    /// <summary>
    /// Internal representation of the JSON response for daily time series.
    /// </summary>
    public sealed class TimeSeriesDailyResponse
    {
        /// <summary>
        /// The time series data keyed by date string (property name is the literal returned by AlphaVantage JSON).
        /// </summary>
        [JsonPropertyName("Time Series (Daily)")]
        public Dictionary<string, BarRaw>? TimeSeries { get; set; }

        /// <summary>
        /// Optional throttle / error note returned by AlphaVantage.
        /// </summary>
        [JsonPropertyName("Note")] public string? Note { get; set; }
        /// <summary>
        /// Optional informational text returned by AlphaVantage.
        /// </summary>
        [JsonPropertyName("Information")] public string? Information { get; set; }
        /// <summary>
        /// Optional error message returned by AlphaVantage for invalid requests.
        /// </summary>
        [JsonPropertyName("Error Message")] public string? Error { get; set; }
    }

    /// <summary>
    /// Raw numeric values of a single OHLCV bar as returned by AlphaVantage.
    /// </summary>
    public sealed class BarRaw
    {
        /// <summary>
        /// Opening price as provided by AlphaVantage (string representation, invariant culture decimal format).
        /// </summary>
        [JsonPropertyName("1. open")] public string? Open { get; set; }

        /// <summary>
        /// Highest price during the period as provided by AlphaVantage (string representation).
        /// </summary>
        [JsonPropertyName("2. high")] public string? High { get; set; }

        /// <summary>
        /// Lowest price during the period as provided by AlphaVantage (string representation).
        /// </summary>
        [JsonPropertyName("3. low")] public string? Low { get; set; }

        /// <summary>
        /// Closing price as provided by AlphaVantage (string representation).
        /// </summary>
        [JsonPropertyName("4. close")] public string? Close { get; set; }

        /// <summary>
        /// Traded volume during the period as provided by AlphaVantage (string representation of integer).
        /// </summary>
        [JsonPropertyName("5. volume")] public string? Volume { get; set; }
    }

    /// <summary>
    /// JSON serializer options used to parse AlphaVantage responses.
    /// </summary>
    /// <returns>The result.</returns>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

/// <summary>
/// Exception thrown when AlphaVantage request limits were exceeded and the client is blocked from issuing further requests for a short period.
/// </summary>
public sealed class RequestLimitExceededException : ApplicationException
{
    /// <summary>
    /// Initializes a new instance of <see cref="RequestLimitExceededException"/> with the specified message.
    /// </summary>
    /// <param name="message">Human readable description of the limit condition.</param>
    public RequestLimitExceededException(string message) : base(message) { }
}