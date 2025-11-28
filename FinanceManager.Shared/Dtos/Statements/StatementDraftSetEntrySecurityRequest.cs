namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to assign security details to a statement draft entry (trade, dividend, fees, taxes).
/// </summary>
public sealed record StatementDraftSetEntrySecurityRequest(Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);
