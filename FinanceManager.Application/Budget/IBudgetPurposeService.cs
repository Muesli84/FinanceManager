using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Service for managing budget purposes.
/// </summary>
public interface IBudgetPurposeService
{
    /// <summary>
    /// Creates a new budget purpose.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="sourceId">The source id.</param>
    /// <param name="description">The description.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="valuationType">The valuation type.</param>
    /// <returns>The result.</returns>
    Task<BudgetPurposeDto> CreateAsync(Guid ownerUserId, string name, BudgetSourceType sourceType, Guid sourceId, string? description, Guid? budgetCategoryId, CancellationToken ct, BudgetValuationType valuationType = BudgetValuationType.ExactPostings);

    /// <summary>
    /// Updates an existing budget purpose.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="sourceId">The source id.</param>
    /// <param name="description">The description.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="valuationType">The valuation type.</param>
    /// <returns>The result.</returns>
    Task<BudgetPurposeDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, BudgetSourceType sourceType, Guid sourceId, string? description, Guid? budgetCategoryId, CancellationToken ct, BudgetValuationType valuationType = BudgetValuationType.ExactPostings);

    /// <summary>
    /// Deletes an existing budget purpose.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Gets a budget purpose by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetPurposeDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists budget purposes.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="nameFilter">The name filter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BudgetPurposeDto>> ListAsync(Guid ownerUserId, int skip, int take, BudgetSourceType? sourceType, string? nameFilter, CancellationToken ct);

    /// <summary>
    /// Returns the total number of purposes for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<int> CountAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists budget purposes including rule count and computed budget sum for the provided period.
    /// When <paramref name="from"/> or <paramref name="to"/> are <c>null</c>, the service may apply a default period.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="nameFilter">The name filter.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="dateBasis">The date basis.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BudgetPurposeOverviewDto>> ListOverviewAsync(
        Guid ownerUserId,
        int skip,
        int take,
        BudgetSourceType? sourceType,
        string? nameFilter,
        DateOnly? from,
        DateOnly? to,
        Guid? budgetCategoryId,
        CancellationToken ct,
        BudgetReportDateBasis dateBasis = BudgetReportDateBasis.BookingDate);
}
