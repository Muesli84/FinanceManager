namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear the cost-neutral flag of a statement draft entry.
/// </summary>
public sealed record StatementDraftSetCostNeutralRequest(bool? IsCostNeutral);
