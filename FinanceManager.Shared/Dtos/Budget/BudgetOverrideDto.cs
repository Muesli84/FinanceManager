namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Data transfer object for budget overrides.
/// </summary>
/// <param name="Id">Override id.</param>
/// <param name="OwnerUserId">Owner user id.</param>
/// <param name="BudgetPurposeId">Purpose id.</param>
/// <param name="PeriodYear">Override year.</param>
/// <param name="PeriodMonth">Override month (1..12).</param>
/// <param name="Amount">Replacement amount.</param>
/// <returns>The result.</returns>
public sealed record BudgetOverrideDto(Guid Id, Guid OwnerUserId, Guid BudgetPurposeId, int PeriodYear, int PeriodMonth, decimal Amount);
