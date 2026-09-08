namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to set or clear the cost-neutral flag of a statement draft entry.
/// </summary>
/// <param name="IsCostNeutral">The is cost neutral.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetCostNeutralRequest(bool? IsCostNeutral);
