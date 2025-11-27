namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Defines the type of a bank account.
/// </summary>
public enum AccountType
{
    /// <summary>Checking/current account.</summary>
    Giro = 0,
    /// <summary>Savings account.</summary>
    Savings = 1
}

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

/// <summary>
/// DTO representing a bank account and its core properties used by the client UI.
/// </summary>
public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string? Iban,
    decimal CurrentBalance,
    Guid BankContactId,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
