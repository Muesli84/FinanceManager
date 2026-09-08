using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Parameter describing a cached budget report raw data range.
/// </summary>
/// <param name="From">The from.</param>
/// <param name="To">The to.</param>
/// <param name="DateBasis">The date basis.</param>
/// <returns>The result.</returns>
public sealed record BudgetReportCacheParameter(DateOnly From, DateOnly To, FinanceManager.Shared.Dtos.Budget.BudgetReportDateBasis DateBasis);
