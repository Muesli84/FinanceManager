namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// Transaction types for security postings (main trade/dividend actions).
/// </summary>
public enum SecurityTransactionType
{
    /// <summary>Buy transaction.</summary>
    Buy = 0,
    /// <summary>Sell transaction.</summary>
    Sell = 1,
    /// <summary>Dividend transaction.</summary>
    Dividend = 2
}