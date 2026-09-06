namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to assign or clear a split draft association on a statement draft entry.
/// </summary>
/// <param name="SplitDraftId">The split draft id.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetSplitDraftRequest(Guid? SplitDraftId);
