namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// Compact return summary for the widget on the security detail page (FR-1).
/// SparklineData is loaded separately.
/// </summary>
/// <param name="InvestedCapital">Total invested capital (sum of all buy amounts).</param>
/// <param name="CurrentMarketValue">Current market value (shares held × current price).</param>
/// <param name="TotalReturnAbsolute">Absolute total return (market value + net dividends - invested capital).</param>
/// <param name="TotalReturnPercent">Percentage total return.</param>
/// <param name="Cagr">Compound Annual Growth Rate, or null if holding period &lt; 1 year.</param>
/// <param name="Irr">Internal Rate of Return (personal return), or null when not computable.</param>
/// <param name="CostBasisPerShare">Average cost per share (FIFO basis).</param>
/// <param name="CurrentPricePerShare">Latest available price per share.</param>
/// <param name="TotalSharesHeld">Total number of shares currently held (FIFO net position).</param>
/// <param name="NetDividends">Net dividends received (after taxes).</param>
/// <param name="CurrencyCode">ISO currency code.</param>
/// <param name="HasMissingPrices">Whether price data gaps exist.</param>
/// <param name="MissingPricesHint">Hint about missing price data, or null.</param>
/// <returns>The result.</returns>
public sealed record ReturnSummaryDto(
    decimal InvestedCapital,
    decimal CurrentMarketValue,
    decimal TotalReturnAbsolute,
    decimal TotalReturnPercent,
    decimal? Cagr,
    decimal? Irr,
    decimal CostBasisPerShare,
    decimal CurrentPricePerShare,
    decimal TotalSharesHeld,
    decimal NetDividends,
    string CurrencyCode,
    bool HasMissingPrices,
    string? MissingPricesHint
);

/// <summary>
/// Detailed return metrics for the Kennzahlen tab (FR-2.1).
/// </summary>
/// <param name="GrossReturn">Gross return before taxes and fees.</param>
/// <param name="NetReturn">Net return after taxes.</param>
/// <param name="TotalTaxes">Total taxes paid.</param>
/// <param name="TotalFees">Total fees paid.</param>
/// <param name="TaxRate">Tax rate as fraction of gross return (0..1).</param>
/// <param name="Twr">Time-Weighted Return (Modified Dietz, GIPS-konform).</param>
/// <param name="Volatility">Annualised volatility (std. dev. of log returns × √252).</param>
/// <param name="MaxDrawdown">Maximum drawdown from peak (negative fraction).</param>
/// <param name="SharpeRatio">Sharpe Ratio, or null when opt-in not enabled or not enough data.</param>
/// <param name="RealizedGains">Realized capital gains (FIFO).</param>
/// <param name="UnrealizedGains">Unrealized capital gains on current holdings.</param>
/// <param name="Irr">Internal Rate of Return, or null when not computable.</param>
/// <param name="DividendYieldCurrentYear">Dividend yield for current calendar year.</param>
/// <returns>The result.</returns>
public sealed record DetailedReturnMetricsDto(
    decimal GrossReturn,
    decimal NetReturn,
    decimal TotalTaxes,
    decimal TotalFees,
    decimal TaxRate,
    decimal? Twr,
    decimal? Volatility,
    decimal? MaxDrawdown,
    decimal? SharpeRatio,
    decimal RealizedGains,
    decimal UnrealizedGains,
    decimal? Irr,
    decimal DividendYieldCurrentYear
);

/// <summary>
/// Formula and cashflow breakdown for a single KPI, used in the info side panel (FR-1).
/// </summary>
/// <param name="KpiKey">Stable internal key matching the widget KPI (e.g. "TotalReturn", "InvestedCapital").</param>
/// <param name="DisplayName">Localized display name of the KPI (e.g. "Gesamtrendite").</param>
/// <param name="FormulaText">Human-readable formula as a complete equation.</param>
/// <param name="Description">Short explanation of what this metric measures.</param>
/// <param name="Groups">Posting groups, each representing one element of the formula.</param>
/// <returns>The result.</returns>
public sealed record KpiBreakdownDto(
    string KpiKey,
    string DisplayName,
    string FormulaText,
    string Description,
    IReadOnlyList<KpiFormulaGroup> Groups
);

/// <summary>
/// A group of cashflow items representing one formula element (e.g. "Dividenden").
/// </summary>
/// <param name="GroupName">Display name of this formula element.</param>
/// <param name="IsPositiveContribution">True when this group increases the KPI value.</param>
/// <param name="Items">Individual cashflow items in this group.</param>
/// <param name="GroupTotal">Sum of all item amounts in this group (absolute monetary value).</param>
/// <param name="GroupTotalPercent">
/// Optional percentage value for this group total (e.g. the percentage return for the
/// "Gesamtrendite" result group). When set, the UI renders both the percentage and the
/// absolute amount side by side in the info-panel result block.
/// </param>
/// <param name="GroupNote">
/// Optional informational text to display instead of a monetary amount in the group header
/// (e.g. the holding period for the "Anlagedauer" group). When set, the UI renders this
/// text in place of the formatted EUR value and omits amount columns from item rows.
/// </param>
/// <returns>The result.</returns>
public sealed record KpiFormulaGroup(
    string GroupName,
    bool IsPositiveContribution,
    IReadOnlyList<KpiBreakdownItem> Items,
    decimal GroupTotal,
    decimal? GroupTotalPercent = null,
    string? GroupNote = null
);

/// <summary>
/// A single cashflow item within a KPI formula group.
/// </summary>
/// <param name="Date">Date of the cashflow.</param>
/// <param name="Amount">Amount displayed for this item. For IRR breakdown groups this is the present value
/// (discounted cashflow); for all other groups it is the raw cashflow amount.</param>
/// <param name="Note">Optional descriptive note (e.g. number of shares).</param>
/// <returns>The result.</returns>
public sealed record KpiBreakdownItem(DateTime Date, decimal Amount, string? Note)
{
    /// <summary>Years since t₀ for discounting (used in IRR breakdown). Null when not applicable.</summary>
    public decimal? YearsSinceT0 { get; init; }

    /// <summary>Discount factor 1/(1+r)^t (used in IRR breakdown). Null when not applicable.</summary>
    public decimal? DiscountFactor { get; init; }

    /// <summary>Original (undiscounted) cashflow amount (used in IRR breakdown). Null when not applicable.</summary>
    public decimal? OriginalCashflow { get; init; }
}

/// <summary>Time range selection for the performance chart (FR-2.4).</summary>
public enum ChartTimeRange
{
    /// <summary>One month time range.</summary>
    OneMonth,

    /// <summary>Three months time range.</summary>
    ThreeMonths,

    /// <summary>Six months time range.</summary>
    SixMonths,

    /// <summary>One year time range.</summary>
    OneYear,

    /// <summary>Three years time range.</summary>
    ThreeYears,

    /// <summary>Entire available history.</summary>
    All
}

/// <summary>A single chart data point.</summary>
/// <param name="Date">Date of the data point.</param>
/// <param name="Value">Value on this date.</param>
/// <returns>The result.</returns>
public sealed record ChartPoint(DateTime Date, decimal Value);

/// <summary>
/// Periodic returns for the Zeitliche Entwicklung tab (FR-2.2 + FR-2.5).
/// </summary>
/// <param name="AnnualReturns">Annual return data points for the bar chart.</param>
/// <param name="MonthlyReturns">Monthly return data points for the heatmap.</param>
/// <param name="AnnualDividends">Annual dividend data for the dividend chart.</param>
/// <param name="HasSimulatedPrices">
/// True when no real price history was available and returns were computed from
/// transaction-implied prices (linear interpolation between buy/sell anchors).
/// Consumers should display a disclaimer in this case.
/// </param>
/// <returns>The result.</returns>
public sealed record PeriodicReturnsDto(
    IReadOnlyList<AnnualReturnPoint> AnnualReturns,
    IReadOnlyList<MonthlyReturnPoint> MonthlyReturns,
    IReadOnlyList<AnnualDividendPoint> AnnualDividends,
    bool HasSimulatedPrices = false
);

/// <summary>Annual return data point.</summary>
/// <param name="Year">Calendar year.</param>
/// <param name="ReturnPercent">Annual return as percentage.</param>
/// <param name="IsYtd">True if this is the current year-to-date figure.</param>
/// <returns>The result.</returns>
public sealed record AnnualReturnPoint(int Year, decimal ReturnPercent, bool IsYtd);

/// <summary>Monthly return data point for the heatmap.</summary>
/// <param name="Year">Calendar year.</param>
/// <param name="Month">Calendar month (1-12).</param>
/// <param name="ReturnPercent">Monthly return as percentage, or null when no data.</param>
/// <returns>The result.</returns>
public sealed record MonthlyReturnPoint(int Year, int Month, decimal? ReturnPercent);

/// <summary>Annual dividend summary.</summary>
/// <param name="Year">Calendar year.</param>
/// <param name="GrossDividend">Gross dividend amount.</param>
/// <param name="NetDividend">Net dividend after taxes.</param>
/// <param name="CumulativeNet">Cumulative net dividends up to and including this year.</param>
/// <returns>The result.</returns>
public sealed record AnnualDividendPoint(int Year, decimal GrossDividend, decimal NetDividend, decimal CumulativeNet);

/// <summary>
/// Cashflow timeline for the Cashflows tab (FR-2.3 + FR-2.6).
/// </summary>
/// <param name="Entries">Chronological cashflow entries.</param>
/// <param name="AnnualSummaries">Annual aggregated cashflows for the bar chart.</param>
/// <returns>The result.</returns>
public sealed record CashflowTimelineDto(
    IReadOnlyList<CashflowEntry> Entries,
    IReadOnlyList<AnnualCashflowSummary> AnnualSummaries
);

/// <summary>A single cashflow entry in the timeline.</summary>
/// <param name="Date">Date of the cashflow.</param>
/// <param name="Type">Cashflow type ("Buy", "Sell", "Dividend", "Tax", "Fee").</param>
/// <param name="Amount">Cashflow amount.</param>
/// <param name="Description">Optional description.</param>
/// <param name="PostingId">Reference to the source posting.</param>
/// <returns>The result.</returns>
public sealed record CashflowEntry(DateTime Date, string Type, decimal Amount, string? Description, Guid PostingId);

/// <summary>Annual cashflow summary for the cost/tax chart.</summary>
/// <param name="Year">Calendar year.</param>
/// <param name="TotalBuys">Total buy cashflows (negative).</param>
/// <param name="TotalSells">Total sell cashflows (positive).</param>
/// <param name="TotalDividends">Total gross dividends.</param>
/// <param name="TotalTaxes">Total taxes (negative).</param>
/// <param name="TotalFees">Total fees (negative).</param>
/// <returns>The result.</returns>
public sealed record AnnualCashflowSummary(int Year, decimal TotalBuys, decimal TotalSells, decimal TotalDividends, decimal TotalTaxes, decimal TotalFees);

/// <summary>
/// Performance chart data for the Übersicht tab (FR-2.4).
/// </summary>
/// <param name="TimeRange">Selected time range.</param>
/// <param name="PortfolioValues">Portfolio market value time series.</param>
/// <param name="InvestedCapitalValues">Invested capital time series.</param>
/// <returns>The result.</returns>
public sealed record PerformanceChartDataDto(
    ChartTimeRange TimeRange,
    IReadOnlyList<ChartPoint> PortfolioValues,
    IReadOnlyList<ChartPoint> InvestedCapitalValues
);

/// <summary>
/// Benchmark comparison data for the Benchmark tab (FR-7).
/// </summary>
/// <param name="BenchmarkSecurityId">Id of the benchmark security.</param>
/// <param name="BenchmarkName">Display name of the benchmark security.</param>
/// <param name="SecurityNormalizedValues">
/// Normalized values for the target security over the full range from <see cref="StartDate"/> to
/// <see cref="EndDate"/>. Values are normalized to 100 at <see cref="ComparisonStartDate"/>, so the
/// security's value before <see cref="ComparisonStartDate"/> represents its performance relative to
/// that anchor point (context zone).
/// </param>
/// <param name="BenchmarkNormalizedValues">
/// Normalized values for the benchmark from <see cref="ComparisonStartDate"/> to <see cref="EndDate"/>,
/// normalized to 100 at <see cref="ComparisonStartDate"/>.
/// </param>
/// <param name="BenchmarkPreOverlapValues">
/// Benchmark values in the pre-overlap zone from <see cref="StartDate"/> to <see cref="ComparisonStartDate"/>
/// (exclusive), normalized using the same base as <see cref="BenchmarkNormalizedValues"/>. The curve is
/// derived from actual benchmark price data (forward-filled) and connects seamlessly at
/// <see cref="ComparisonStartDate"/> = 100. Empty when the benchmark has no price data before
/// <see cref="ComparisonStartDate"/> (in that case the chart renders a flat reference line instead).
/// </param>
/// <param name="StartDate">Start date of the security's history (first transaction date).</param>
/// <param name="ComparisonStartDate">
/// The first date on which both the security and the benchmark have price data. Both series are
/// normalized to 100 at this date. Before this date only the security context line is shown.
/// Equals <see cref="StartDate"/> when both series start simultaneously.
/// </param>
/// <param name="EndDate">End date of the comparison period (last sell date when fully sold, otherwise today).</param>
/// <returns>The result.</returns>
public sealed record BenchmarkComparisonDto(
    Guid BenchmarkSecurityId,
    string BenchmarkName,
    IReadOnlyList<ChartPoint> SecurityNormalizedValues,
    IReadOnlyList<ChartPoint> BenchmarkNormalizedValues,
    IReadOnlyList<ChartPoint> BenchmarkPreOverlapValues,
    DateTime StartDate,
    DateTime ComparisonStartDate,
    DateTime EndDate
);

/// <summary>Request payload for updating return analysis settings via API client.</summary>
/// <param name="BenchmarkSecurityId">Optional benchmark security id. Null clears the benchmark.</param>
/// <param name="ShowSharpeRatio">Whether to show Sharpe Ratio in the UI.</param>
/// <param name="RiskFreeRate">Risk-free rate for Sharpe Ratio calculation (e.g. 0.04 = 4%). Must be >= 0.</param>
/// <returns>The result.</returns>
public sealed record ReturnAnalysisSettingsUpdateRequest(
    Guid? BenchmarkSecurityId,
    bool ShowSharpeRatio,
    decimal RiskFreeRate
);

/// <summary>Return analysis settings for the current user (client-side response DTO).</summary>
/// <param name="BenchmarkSecurityId">Optional benchmark security id. Null means no benchmark set.</param>
/// <param name="BenchmarkSecurityName">Display name of the benchmark security, or null when none set.</param>
/// <param name="ShowSharpeRatio">Whether to show Sharpe Ratio in the UI.</param>
/// <param name="RiskFreeRate">Risk-free rate for Sharpe Ratio calculation.</param>
/// <returns>The result.</returns>
public sealed record ReturnAnalysisSettingsResponse(
    Guid? BenchmarkSecurityId,
    string? BenchmarkSecurityName,
    bool ShowSharpeRatio,
    decimal RiskFreeRate
);

/// <summary>
/// Sparkline data for the mini-chart (FR-1.1). Loaded separately to keep ReturnSummaryDto lean.
/// </summary>
/// <param name="Points">Time series of (date, value) pairs showing invested capital vs. market value.</param>
/// <returns>The result.</returns>
public sealed record SparklineDataDto(IReadOnlyList<SparklinePoint> Points);

/// <summary>A single point in the sparkline chart.</summary>
/// <param name="Date">Date of the data point.</param>
/// <param name="MarketValue">Portfolio market value on this date.</param>
/// <param name="InvestedCapital">Cumulative invested capital on this date.</param>
/// <returns>The result.</returns>
public sealed record SparklinePoint(DateTime Date, decimal MarketValue, decimal InvestedCapital);
