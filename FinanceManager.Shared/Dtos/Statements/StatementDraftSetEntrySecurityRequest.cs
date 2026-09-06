namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to assign security details to a statement draft entry (trade, dividend, fees, taxes).
/// </summary>
/// <param name="SecurityId">The security id.</param>
/// <param name="TransactionType">The transaction type.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="FeeAmount">The fee amount.</param>
/// <param name="TaxAmount">The tax amount.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetEntrySecurityRequest(Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);
