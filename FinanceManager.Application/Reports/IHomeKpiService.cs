namespace FinanceManager.Application.Reports;

/// <summary>
/// Service to manage home page KPI widgets (create, list, update, delete) for a user.
/// </summary>
public interface IHomeKpiService
{
    /// <summary>
    /// Lists home KPIs for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<HomeKpiDto>> ListAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Creates a new home KPI for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<HomeKpiDto> CreateAsync(Guid ownerUserId, HomeKpiCreateRequest request, CancellationToken ct);

    /// <summary>
    /// Updates an existing home KPI and returns the updated DTO or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="request">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<HomeKpiDto?> UpdateAsync(Guid id, Guid ownerUserId, HomeKpiUpdateRequest request, CancellationToken ct);

    /// <summary>
    /// Deletes a home KPI.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}

