namespace FinanceManager.Web.ViewModels.Reports;

/// <summary>
/// Aggregated totals of the currently visible top-level report rows.
/// </summary>
/// <param name="Amount">Sum of the current period amounts.</param>
/// <param name="Projection">Sum of the projected amounts, or <c>null</c> when projection is not shown.</param>
/// <param name="Prev">Sum of the previous comparison period, or <c>null</c> when not shown.</param>
/// <param name="Year">Sum of the year-ago comparison period, or <c>null</c> when not shown.</param>
/// <returns>The result.</returns>
public readonly record struct ReportTotals(decimal Amount, decimal? Projection, decimal? Prev, decimal? Year);
