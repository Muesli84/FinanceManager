namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Optional top-level filters for report aggregation. Filters are applied on the top level only
/// (either category or entity, depending on IncludeCategory/kind capabilities).
/// All collections are treated as allow-lists; null or empty = no filtering for that dimension.
/// </summary>
/// <param name="AccountIds">Optional allowed account ids.</param>
/// <param name="ContactIds">Optional allowed contact ids.</param>
/// <param name="SavingsPlanIds">Optional allowed savings plan ids.</param>
/// <param name="SecurityIds">Optional allowed security ids.</param>
/// <param name="ContactCategoryIds">Optional allowed contact category ids.</param>
/// <param name="SavingsPlanCategoryIds">Optional allowed savings plan category ids.</param>
/// <param name="SecurityCategoryIds">Optional allowed security category ids.</param>
/// <param name="SecuritySubTypes">Optional allowed security posting sub types (e.g., Buy/Sell/Dividend/Fee/Tax).</param>
/// <param name="IncludeDividendRelated">When true, include Fee/Tax from dividend groups (net dividend).</param>
public sealed record ReportAggregationFilters(
    IReadOnlyCollection<Guid>? AccountIds = null,
    IReadOnlyCollection<Guid>? ContactIds = null,
    IReadOnlyCollection<Guid>? SavingsPlanIds = null,
    IReadOnlyCollection<Guid>? SecurityIds = null,
    IReadOnlyCollection<Guid>? ContactCategoryIds = null,
    IReadOnlyCollection<Guid>? SavingsPlanCategoryIds = null,
    IReadOnlyCollection<Guid>? SecurityCategoryIds = null,
    IReadOnlyCollection<int>? SecuritySubTypes = null,
    bool? IncludeDividendRelated = null
);
