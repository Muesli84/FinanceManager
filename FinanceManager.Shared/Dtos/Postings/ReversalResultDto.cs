namespace FinanceManager.Shared.Dtos.Postings;

/// <summary>
/// Result of a posting reversal operation containing information about reversed and created postings.
/// </summary>
/// <param name="ReversedPostingIds">List of posting IDs that were reversed (original postings).</param>
/// <param name="CreatedReversalIds">List of posting IDs that were created as reversals (new cancellation postings).</param>
/// <param name="StatementDraftId">ID of the statement draft created for reconciliation purposes.</param>
/// <returns>The result.</returns>
public sealed record ReversalResultDto(
    IReadOnlyList<Guid> ReversedPostingIds,
    IReadOnlyList<Guid> CreatedReversalIds,
    Guid StatementDraftId);
