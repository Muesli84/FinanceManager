using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request payload for creating a budget rule.
/// Exactly one of <see cref="BudgetPurposeId"/> or <see cref="BudgetCategoryId"/> must be provided.
/// </summary>
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
public sealed record BudgetRuleCreateRequest(
    Guid? BudgetPurposeId,
    Guid? BudgetCategoryId,
    decimal Amount,
    BudgetIntervalType Interval,
    int? CustomIntervalMonths,
    DateOnly StartDate,
    DateOnly? EndDate,
    [MaxLength(500)] string? PurposePattern = null,
    bool UseRegex = false)
{
}
