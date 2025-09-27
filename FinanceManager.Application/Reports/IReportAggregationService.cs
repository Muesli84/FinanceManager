using FinanceManager.Domain.Reports;

namespace FinanceManager.Application.Reports;

/// <summary>
/// Provides dynamic aggregation for configurable reports (FA-REP-008).
/// </summary>
public interface IReportAggregationService
{
    Task<ReportAggregationResult> QueryAsync(ReportAggregationQuery query, CancellationToken ct);
}

/// <summary>
/// Optional top-level filters for report aggregation. Filters are applied on the top level only
/// (either category or entity depending on IncludeCategory/kind capabilities).
/// All collections are treated as allow-lists; null or empty = no filtering for that dimension.
/// </summary>
public sealed record ReportAggregationFilters(
    IReadOnlyCollection<Guid>? AccountIds = null,
    IReadOnlyCollection<Guid>? ContactIds = null,
    IReadOnlyCollection<Guid>? SavingsPlanIds = null,
    IReadOnlyCollection<Guid>? SecurityIds = null,
    IReadOnlyCollection<Guid>? ContactCategoryIds = null,
    IReadOnlyCollection<Guid>? SavingsPlanCategoryIds = null,
    IReadOnlyCollection<Guid>? SecurityCategoryIds = null
);

public sealed record ReportAggregationQuery(
    Guid OwnerUserId,
    int PostingKind,
    ReportInterval Interval,
    int Take,
    bool IncludeCategory,
    bool ComparePrevious,
    bool CompareYear,
    IReadOnlyCollection<int>? PostingKinds = null, // multi
    DateTime? AnalysisDate = null, // optional analysis month
    ReportAggregationFilters? Filters = null // optional entity/category filters
);

public sealed record ReportAggregatePointDto(
    DateTime PeriodStart,
    string GroupKey,
    string GroupName,
    string? CategoryName,
    decimal Amount,
    string? ParentGroupKey,
    decimal? PreviousAmount,
    decimal? YearAgoAmount);

public sealed record ReportAggregationResult(
    ReportInterval Interval,
    IReadOnlyList<ReportAggregatePointDto> Points,
    bool ComparedPrevious,
    bool ComparedYear);
