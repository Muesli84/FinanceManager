namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// A single aggregated data point.
/// </summary>
/// <param name="PeriodStart">Start of the period (e.g., first day of month).</param>
/// <param name="GroupKey">Stable key of the aggregated group.</param>
/// <param name="GroupName">Display name of the aggregated group.</param>
/// <param name="CategoryName">Optional category name of the group.</param>
/// <param name="Amount">Aggregated amount for the period/group.</param>
/// <param name="ProjectionAmount">Optional projected amount for dividend reports.</param>
/// <param name="ParentGroupKey">Optional parent group key for hierarchical results.</param>
/// <param name="PreviousAmount">Optional amount in the previous comparison period.</param>
/// <param name="YearAgoAmount">Optional amount in the same period one year ago.</param>
/// <param name="ProjectionExpectedDividends">Expected dividend details contributing to the projection.</param>
/// <returns>The result.</returns>
public sealed record ReportAggregatePointDto(
    DateTime PeriodStart,
    string GroupKey,
    string GroupName,
    string? CategoryName,
    decimal Amount,
    decimal? ProjectionAmount,
    string? ParentGroupKey,
    decimal? PreviousAmount,
    decimal? YearAgoAmount,
    IReadOnlyList<ReportProjectionExpectedDividendDto>? ProjectionExpectedDividends = null
);

/// <summary>
/// Details for a prior-year dividend that is still expected in the projected report period.
/// </summary>
/// <param name="SecurityId">The security id.</param>
/// <param name="SecurityName">The security name.</param>
/// <param name="ExpectedDate">The expected date.</param>
/// <param name="PriorYearDate">The prior year date.</param>
/// <param name="Amount">The amount.</param>
/// <returns>The result.</returns>
public sealed record ReportProjectionExpectedDividendDto(
    Guid SecurityId,
    string SecurityName,
    DateTime ExpectedDate,
    DateTime PriorYearDate,
    decimal Amount);
