namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Overall status of a statement draft.
/// </summary>
public enum StatementDraftStatus
{
    /// <summary>Draft is open and can be edited.</summary>
    Draft = 0,
    /// <summary>Draft has been committed and posted.</summary>
    Committed = 1,
    /// <summary>Draft expired and is no longer valid.</summary>
    Expired = 2
}
