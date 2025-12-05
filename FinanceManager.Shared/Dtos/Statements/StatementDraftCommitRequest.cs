namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to commit an existing statement draft to postings.
/// </summary>
public sealed record StatementDraftCommitRequest(Guid AccountId, ImportFormat Format);
