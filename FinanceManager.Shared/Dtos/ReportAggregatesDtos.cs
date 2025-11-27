namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload describing filters to apply when querying report aggregates.
/// </summary>
public sealed record ReportAggregatesFiltersRequest(
    IReadOnlyCollection<Guid>? AccountIds,
    IReadOnlyCollection<Guid>? ContactIds,
    IReadOnlyCollection<Guid>? SavingsPlanIds,
    IReadOnlyCollection<Guid>? SecurityIds,
    IReadOnlyCollection<Guid>? ContactCategoryIds,
    IReadOnlyCollection<Guid>? SavingsPlanCategoryIds,
    IReadOnlyCollection<Guid>? SecurityCategoryIds,
    IReadOnlyCollection<int>? SecuritySubTypes,
    bool? IncludeDividendRelated
);


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
