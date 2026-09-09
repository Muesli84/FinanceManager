namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request payload for creating a budget override.
/// </summary>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <param name="Period">The period.</param>
/// <param name="Amount">The amount.</param>
/// <returns>The result.</returns>
public sealed record BudgetOverrideCreateRequest(
    Guid BudgetPurposeId,
    BudgetPeriodKey Period,
    decimal Amount);
