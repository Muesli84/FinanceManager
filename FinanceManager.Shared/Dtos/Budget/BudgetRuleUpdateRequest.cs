using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request payload for updating a budget rule.
/// Target association (purpose/category) is not updatable.
/// </summary>
/// <param name="Amount">The amount.</param>
/// <param name="Interval">The interval.</param>
/// <param name="CustomIntervalMonths">The custom interval months.</param>
/// <param name="StartDate">The start date.</param>
/// <param name="EndDate">The end date.</param>
/// <param name="PurposePattern">The purpose pattern.</param>
/// <param name="UseRegex">The use regex.</param>
/// <returns>The result.</returns>
public sealed record BudgetRuleUpdateRequest(
    decimal Amount,
    BudgetIntervalType Interval,
    int? CustomIntervalMonths,
    DateOnly StartDate,
    DateOnly? EndDate,
    [MaxLength(500)] string? PurposePattern = null,
    bool UseRegex = false)
{
}
