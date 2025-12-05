namespace FinanceManager.Application.Reports;

/// <summary>
/// Provides dynamic aggregation for configurable reports (FA-REP-008).
/// </summary>
public interface IReportAggregationService
{
    /// <summary>
    /// Executes an aggregation query and returns the result.
    /// </summary>
    /// <param name="query">The aggregation query payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregation result with points and comparison flags.</returns>
    Task<ReportAggregationResult> QueryAsync(ReportAggregationQuery query, CancellationToken ct);
}
