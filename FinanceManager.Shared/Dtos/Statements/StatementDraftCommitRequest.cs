namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to commit an existing statement draft to postings.
/// </summary>
/// <param name="AccountId">The account id.</param>
/// <param name="Format">The format.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftCommitRequest(Guid AccountId, ImportFormat Format);
