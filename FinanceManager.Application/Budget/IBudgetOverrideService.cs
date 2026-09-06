using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Service for managing budget overrides.
/// </summary>
public interface IBudgetOverrideService
{
    /// <summary>
    /// Creates a new override for a given purpose and period.
    /// If an override for the same period already exists, the operation should fail.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="period">The period.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetOverrideDto> CreateAsync(Guid ownerUserId, Guid budgetPurposeId, BudgetPeriodKey period, decimal amount, CancellationToken ct);

    /// <summary>
    /// Updates an existing override.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="period">The period.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetOverrideDto?> UpdateAsync(Guid id, Guid ownerUserId, BudgetPeriodKey period, decimal amount, CancellationToken ct);

    /// <summary>
    /// Deletes an existing override.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Gets an override by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetOverrideDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists overrides for a purpose.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BudgetOverrideDto>> ListByPurposeAsync(Guid ownerUserId, Guid budgetPurposeId, CancellationToken ct);
}
