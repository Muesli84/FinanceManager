namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear the contact association of a statement draft entry.
/// </summary>
public sealed record StatementDraftSetContactRequest(Guid? ContactId);
