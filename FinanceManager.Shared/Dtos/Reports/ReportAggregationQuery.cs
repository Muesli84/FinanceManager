namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Query describing how to aggregate report data.
/// </summary>
/// <param name="OwnerUserId">Owner of the data to aggregate.</param>
/// <param name="PostingKind">Primary posting kind to aggregate.</param>
/// <param name="Interval">Aggregation interval (e.g., Month, Year).</param>
/// <param name="Take">Maximum number of groups to return.</param>
/// <param name="IncludeCategory">True to aggregate on category level when supported.</param>
/// <param name="ComparePrevious">True to include a previous-period comparison.</param>
/// <param name="CompareYear">True to include a year-ago comparison.</param>
/// <param name="PostingKinds">Optional multi-kind extension (falls back to single kind when omitted).</param>
/// <param name="AnalysisDate">Optional analysis month (first day is used as anchor).</param>
/// <param name="UseValutaDate">When true, aggregate by ValutaDate, falling back to BookingDate where needed.</param>
/// <param name="Filters">Optional top-level filters.</param>
public sealed record ReportAggregationQuery(
    Guid OwnerUserId,
    PostingKind PostingKind,
    ReportInterval Interval,
    int Take,
    bool IncludeCategory,
    bool ComparePrevious,
    bool CompareYear,
    IReadOnlyCollection<PostingKind>? PostingKinds = null,
    DateTime? AnalysisDate = null,
    bool UseValutaDate = false,
    ReportAggregationFilters? Filters = null
);
