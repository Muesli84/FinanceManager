using FinanceManager.Shared.Dtos.Statements;

namespace FinanceManager.Application.Statements;

/// <summary>
/// Evaluates budget impact for statement draft interactions and booking completion.
/// </summary>
public interface IBudgetImpactEvaluationService
{
    /// <summary>
    /// Evaluates budget impact for a single draft entry.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetImpactEvaluationDto?> EvaluateEntryImpactAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Evaluates budget impact summary for final booking scope.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BookingImpactSummaryDto?> EvaluateDraftImpactAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct);
}
