namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear the contact association of a statement draft entry.
/// </summary>
/// <param name="ContactId">The contact id.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetContactRequest(Guid? ContactId);
