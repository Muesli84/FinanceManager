namespace FinanceManager.Web.ViewModels.Common
{
    /// <summary>
    /// View model that loads and holds aggregated time-series data for rendering a bar chart component.
    /// </summary>
    public sealed class AggregateBarChartViewModel : BaseViewModel
    {
        private readonly HttpClient _http;
        private readonly string _endpointBase;
        private readonly int? _take;
        private readonly int? _maxYearsBack;
        private readonly string _title;

        /// <summary>
        /// Initializes a new instance of the <see cref="AggregateBarChartViewModel"/> class.
        /// </summary>
        /// <param name="services">Service provider used to resolve dependencies (must provide <see cref="HttpClient"/>).</param>
        /// <param name="endpointBase">Base endpoint URL used to fetch the time series data. May be empty or null, in which case loading is a no-op.</param>
        /// <param name="title">Optional chart title.</param>
        /// <param name="take">Optional maximum number of points to request from the API.</param>
        /// <param name="maxYearsBack">Optional maximum number of years to include in the response.</param>
        /// <exception cref="InvalidOperationException">Thrown when <see cref="HttpClient"/> cannot be resolved from <paramref name="services"/>.</exception>
        public AggregateBarChartViewModel(IServiceProvider services, string endpointBase, string? title = null, int? take = null, int? maxYearsBack = null)
            : base(services)
        {
            _http = services.GetRequiredService<HttpClient>();
            _endpointBase = endpointBase ?? string.Empty;
            _title = title ?? string.Empty;
            _take = take;
            _maxYearsBack = maxYearsBack;
            Data = new List<TimeSeriesPoint>();
            // defaults matching previous component defaults
            PositiveColor = "#2d6cdf";
            NegativeColor = "#c94";
            AxisColor = "#555";
            HideIntervalSelector = false;
            InitialPeriod = "Month";
            AutoFit = true;
            MaxVisibleLabels = 18;
            Compact = false;
            BarsHeightPx = null;
        }

        /// <summary>
        /// Chart title displayed in the UI.
        /// </summary>
        public override string Title => _title;

        /// <summary>
        /// Color used to render positive bars (hex string).
        /// </summary>
        public string PositiveColor { get; set; }

        /// <summary>
        /// Color used to render negative bars (hex string).
        /// </summary>
        public string NegativeColor { get; set; }

        /// <summary>
        /// Color used for chart axis and tick marks (hex string).
        /// </summary>
        public string AxisColor { get; set; }

        /// <summary>
        /// Optional maximum number of items to request from the API.
        /// </summary>
        public int? Take => _take;

        /// <summary>
        /// Optional maximum number of years to include in the response.
        /// </summary>
        public int? MaxYearsBack => _maxYearsBack;

        /// <summary>
        /// When true the UI hides the interval selector control.
        /// </summary>
        public bool HideIntervalSelector { get; set; }

        /// <summary>
        /// Initial period string (e.g. "Month", "Quarter").
        /// </summary>
        public string? InitialPeriod { get; set; }

        /// <summary>
        /// When true the chart attempts to auto-fit labels and scaling.
        /// </summary>
        public bool AutoFit { get; set; }

        /// <summary>
        /// Maximum number of visible labels on the x-axis before simplification occurs.
        /// </summary>
        public int MaxVisibleLabels { get; set; }

        /// <summary>
        /// When true a compact rendering mode is used.
        /// </summary>
        public bool Compact { get; set; }

        /// <summary>
        /// Optional fixed bar height in pixels. When <c>null</c> a responsive height is used.
        /// </summary>
        public int? BarsHeightPx { get; set; }

        // Loading is inherited from BaseViewModel (protected set) - no local redeclaration needed.

        /// <summary>
        /// True when the loaded data contains points from more than one year.
        /// </summary>
        public bool MultipleYears { get; private set; }

        /// <summary>
        /// The loaded time series data points used by the chart component.
        /// </summary>
        public IReadOnlyList<TimeSeriesPoint> Data { get; private set; }

        /// <summary>
        /// Represents a single time series data point (start of period and aggregated amount).
        /// </summary>
        /// <param name="PeriodStart">Start date/time of the period.</param>
        /// <param name="Amount">Aggregated amount for the period.</param>
        /// <returns>The result.</returns>
        public record TimeSeriesPoint(DateTime PeriodStart, decimal Amount);

        /// <summary>
        /// Loads time series data for the specified period from the configured endpoint and updates <see cref="Data"/>
        /// </summary>
        /// <param name="period">Period identifier used by the API (e.g. "Month" or "Quarter").</param>
        /// <returns>A task that completes when the load operation has finished. On failure the <see cref="Data"/> collection is cleared.</returns>
        public async Task LoadAsync(string period)
        {
            if (string.IsNullOrWhiteSpace(_endpointBase)) return;
            Loading = true; RaiseStateChanged();
            try
            {
                var url = _endpointBase.Contains('?') ? $"{_endpointBase}&period={period}" : $"{_endpointBase}?period={period}";
                if (_take.HasValue && _take.Value > 0) url += $"&take={_take.Value}";
                if (_maxYearsBack.HasValue && _maxYearsBack.Value > 0) url += $"&maxYearsBack={_maxYearsBack.Value}";
                var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                {
                    var list = await resp.Content.ReadFromJsonAsync<List<TimeSeriesResponse>>() ?? new List<TimeSeriesResponse>();
                    var pts = list.Select(x => new TimeSeriesPoint(x.PeriodStart, x.Amount)).ToList();
                    Data = pts;
                    MultipleYears = Data.Select(d => d.PeriodStart.Year).Distinct().Skip(1).Any();
                }
                else
                {
                    Data = new List<TimeSeriesPoint>();
                    MultipleYears = false;
                }
            }
            catch
            {
                Data = new List<TimeSeriesPoint>();
                MultipleYears = false;
            }
            finally
            {
                Loading = false; RaiseStateChanged();
            }
        }

        private sealed class TimeSeriesResponse { public DateTime PeriodStart { get; set; } public decimal Amount { get; set; } }
    }
}
