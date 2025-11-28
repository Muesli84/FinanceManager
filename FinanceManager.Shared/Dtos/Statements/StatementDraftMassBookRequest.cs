namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Options used to trigger mass booking of open statement drafts.
/// </summary>
public sealed record StatementDraftMassBookRequest(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);
