namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Aggregation result containing points and comparison flags.
/// </summary>
/// <param name="Interval">The used interval.</param>
/// <param name="Points">Aggregated points.</param>
/// <param name="ComparedPrevious">True when previous-period comparison amounts are present.</param>
/// <param name="ComparedYear">True when year-ago comparison amounts are present.</param>
public sealed record ReportAggregationResult(
    ReportInterval Interval,
    IReadOnlyList<ReportAggregatePointDto> Points,
    bool ComparedPrevious,
    bool ComparedYear
);
