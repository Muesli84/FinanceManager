namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Options used to trigger mass booking of open statement drafts.
/// </summary>
/// <param name="IgnoreWarnings">The ignore warnings.</param>
/// <param name="AbortOnFirstIssue">The abort on first issue.</param>
/// <param name="BookEntriesIndividually">The book entries individually.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftMassBookRequest(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);
