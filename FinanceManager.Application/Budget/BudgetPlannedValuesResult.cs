using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Planned value calculation result.
/// </summary>
/// <param name="From">The from.</param>
/// <param name="To">The to.</param>
/// <param name="Values">The values.</param>
/// <returns>The result.</returns>
public sealed record BudgetPlannedValuesResult(
    BudgetPeriodKey From,
    BudgetPeriodKey To,
    IReadOnlyList<BudgetPlannedValue> Values)
{
    /// <summary>
    /// Returns planned value for a specific purpose and period.
    /// </summary>
    /// <param name="purposeId">The purpose id.</param>
    /// <param name="period">The period.</param>
    /// <returns>The result.</returns>
    public decimal GetPlanned(Guid purposeId, BudgetPeriodKey period)
    {
        var match = Values.FirstOrDefault(v => v.BudgetPurposeId == purposeId && v.Period == period);
        return match?.Amount ?? 0m;
    }
}

/// <summary>
/// A planned amount for a budget purpose and period.
/// </summary>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <param name="Period">The period.</param>
/// <param name="Amount">The amount.</param>
/// <returns>The result.</returns>
public sealed record BudgetPlannedValue(Guid BudgetPurposeId, BudgetPeriodKey Period, decimal Amount);
