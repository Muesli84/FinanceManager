using FinanceManager.Shared.Dtos.Postings;

namespace FinanceManager.Shared.Dtos.Reports;

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
