namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Overview DTO for budget category list pages.
/// </summary>
/// <param name="Id">Identifier of the entity.</param>
/// <param name="Name">The name.</param>
/// <param name="Budget">The budget.</param>
/// <param name="Actual">The actual.</param>
/// <param name="Delta">The delta.</param>
/// <param name="PurposeCount">The purpose count.</param>
/// <returns>The result.</returns>
public sealed record BudgetCategoryOverviewDto(
    Guid Id,
    string Name,
    decimal Budget,
    decimal Actual,
    decimal Delta,
    int PurposeCount);
