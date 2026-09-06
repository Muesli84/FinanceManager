namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request payload for updating a budget override.
/// </summary>
/// <param name="Period">The period.</param>
/// <param name="Amount">The amount.</param>
/// <returns>The result.</returns>
public sealed record BudgetOverrideUpdateRequest(
    BudgetPeriodKey Period,
    decimal Amount);
