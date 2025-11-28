namespace FinanceManager.Shared.Dtos.Reports;

/// <summary>
/// Filter payload stored with a report favorite.
/// </summary>
/// <param name="AccountIds">Optional account ids filter.</param>
/// <param name="ContactIds">Optional contact ids filter.</param>
/// <param name="SavingsPlanIds">Optional savings plan ids filter.</param>
/// <param name="SecurityIds">Optional security ids filter.</param>
/// <param name="ContactCategoryIds">Optional contact category ids filter.</param>
/// <param name="SavingsPlanCategoryIds">Optional savings plan category ids filter.</param>
/// <param name="SecurityCategoryIds">Optional security category ids filter.</param>
/// <param name="SecuritySubTypes">Optional security posting sub-types filter.</param>
/// <param name="IncludeDividendRelated">True when dividend related postings should be included.</param>
public sealed record ReportFavoriteFiltersDto(
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
