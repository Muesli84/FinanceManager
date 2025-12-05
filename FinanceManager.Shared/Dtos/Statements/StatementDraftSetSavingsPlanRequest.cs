namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear an associated savings plan for a statement draft entry.
/// </summary>
public sealed record StatementDraftSetSavingsPlanRequest(Guid? SavingsPlanId);
