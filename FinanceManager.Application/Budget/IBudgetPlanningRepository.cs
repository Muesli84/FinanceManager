using FinanceManager.Domain.Budget;
using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Repository abstraction for loading budget planning data (purposes, rules and overrides).
/// </summary>
public interface IBudgetPlanningRepository
{
    /// <summary>
    /// Loads purpose ids for the provided owner and optional filter set.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="purposeIds">Optional filter set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of purpose ids that exist for the owner.</returns>
    Task<IReadOnlyList<Guid>> GetPurposeIdsAsync(Guid ownerUserId, IReadOnlyCollection<Guid>? purposeIds, CancellationToken ct);

    /// <summary>
    /// Loads rules and overrides relevant for the provided purpose ids and period range.
    /// </summary>
    /// <param name="Rules">The rules.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="purposeIds">Purpose ids.</param>
    /// <param name="from">Start period (inclusive).</param>
    /// <param name="to">End period (inclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of rules and overrides.</returns>
    Task<(IReadOnlyList<BudgetRule> Rules, IReadOnlyList<BudgetOverride> Overrides)> GetRulesAndOverridesAsync(
        Guid ownerUserId,
        IReadOnlyList<Guid> purposeIds,
        BudgetPeriodKey from,
        BudgetPeriodKey to,
        CancellationToken ct);
}
