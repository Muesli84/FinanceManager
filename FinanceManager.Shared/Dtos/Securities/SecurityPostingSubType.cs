namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// Posting sub-types used for detailed security posting categorization.
/// </summary>
public enum SecurityPostingSubType
{
    /// <summary>Buy.</summary>
    Buy = 0,
    /// <summary>Sell.</summary>
    Sell = 1,
    /// <summary>Dividend.</summary>
    Dividend = 2,
    /// <summary>Fee.</summary>
    Fee = 3,
    /// <summary>Tax.</summary>
    Tax = 4
}