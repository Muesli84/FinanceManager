namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to assign or clear a split draft association on a statement draft entry.
/// </summary>
public sealed record StatementDraftSetSplitDraftRequest(Guid? SplitDraftId);
