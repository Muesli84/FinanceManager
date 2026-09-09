namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Request payload describing filters to apply when querying report aggregates.
/// </summary>
/// <param name="AccountIds">The account ids.</param>
/// <param name="ContactIds">The contact ids.</param>
/// <param name="SavingsPlanIds">The savings plan ids.</param>
/// <param name="SecurityIds">The security ids.</param>
/// <param name="ContactCategoryIds">The contact category ids.</param>
/// <param name="SavingsPlanCategoryIds">The savings plan category ids.</param>
/// <param name="SecurityCategoryIds">The security category ids.</param>
/// <param name="SecuritySubTypes">The security sub types.</param>
/// <param name="IncludeDividendRelated">The include dividend related.</param>
/// <returns>The result.</returns>
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
