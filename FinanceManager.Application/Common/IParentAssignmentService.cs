using FinanceManager.Shared.Dtos.Common;

namespace FinanceManager.Application.Common;

/// <summary>
/// Applies an optional parent assignment for a newly created entity.
/// Implementations must validate ownership/permissions.
/// </summary>
public interface IParentAssignmentService
{
    /// <summary>
    /// Assigns the created entity to the specified parent context.
    /// Returns <c>true</c> when the assignment was performed.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="parent">The parent.</param>
    /// <param name="createdKind">The created kind.</param>
    /// <param name="createdId">The created id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> TryAssignAsync(Guid ownerUserId, ParentLinkRequest? parent, string createdKind, Guid createdId, CancellationToken ct);
}
