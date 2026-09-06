namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear an associated savings plan for a statement draft entry.
/// </summary>
/// <param name="SavingsPlanId">The savings plan id.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetSavingsPlanRequest(Guid? SavingsPlanId);
