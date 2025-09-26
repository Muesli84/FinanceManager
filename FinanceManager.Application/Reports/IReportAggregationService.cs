using FinanceManager.Domain.Reports;

namespace FinanceManager.Application.Reports;

/// <summary>
/// Provides dynamic aggregation for configurable reports (FA-REP-008).
/// </summary>
public interface IReportAggregationService
{
    Task<ReportAggregationResult> QueryAsync(ReportAggregationQuery query, CancellationToken ct);
}

public sealed record ReportAggregationQuery(
    Guid OwnerUserId,
    int PostingKind,
    ReportInterval Interval,
    int Take,
    bool IncludeCategory,
    bool ComparePrevious,
    bool CompareYear);

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
