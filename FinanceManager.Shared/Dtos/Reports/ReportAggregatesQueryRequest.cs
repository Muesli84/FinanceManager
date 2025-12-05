namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Request payload to query aggregate reports for a given context.
/// </summary>
public sealed record ReportAggregatesQueryRequest(
    PostingKind PostingKind,
    ReportInterval Interval,
    int Take = 24,
    bool IncludeCategory = false,
    bool ComparePrevious = false,
    bool CompareYear = false,
    bool UseValutaDate = false,
    IReadOnlyCollection<PostingKind>? PostingKinds = null,
    DateTime? AnalysisDate = null,
    ReportAggregatesFiltersRequest? Filters = null
);
