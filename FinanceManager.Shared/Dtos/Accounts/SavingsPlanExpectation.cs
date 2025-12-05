namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Defines the expectation regarding savings plans on an account.
/// </summary>
public enum SavingsPlanExpectation : short
{
    /// <summary>No savings plan expected.</summary>
    None = 0,
    /// <summary>Savings plans are optional.</summary>
    Optional = 1,
    /// <summary>Savings plans are required.</summary>
    Required = 2
}
