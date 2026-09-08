namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Represents a budget rule which can apply either to a specific budget purpose or to a whole budget category.
/// </summary>
/// <param name="Id">Identifier of the entity.</param>
/// <param name="OwnerUserId">The owner user id.</param>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <param name="BudgetCategoryId">The budget category id.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Interval">The interval.</param>
/// <param name="CustomIntervalMonths">The custom interval months.</param>
/// <param name="StartDate">The start date.</param>
/// <param name="EndDate">The end date.</param>
/// <param name="PurposePattern">The purpose pattern.</param>
/// <param name="UseRegex">The use regex.</param>
/// <returns>The result.</returns>
public sealed record BudgetRuleDto(
    Guid Id,
    Guid OwnerUserId,
    Guid? BudgetPurposeId,
    Guid? BudgetCategoryId,
    decimal Amount,
    BudgetIntervalType Interval,
    int? CustomIntervalMonths,
    DateOnly StartDate,
    DateOnly? EndDate,
    string? PurposePattern = null,
    bool UseRegex = false);
