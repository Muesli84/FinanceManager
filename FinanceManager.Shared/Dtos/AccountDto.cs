namespace FinanceManager.Shared.Dtos;

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
/// <param name="Id">Unique account identifier.</param>
/// <param name="Name">Display name of the account.</param>
/// <param name="Type">Account type (e.g., Giro or Savings).</param>
/// <param name="Iban">Optional international bank account number (IBAN).</param>
/// <param name="CurrentBalance">Current balance used for display purposes.</param>
/// <param name="BankContactId">Identifier of the associated bank contact.</param>
/// <param name="SymbolAttachmentId">Attachment id of the current symbol associated with the account.</param>
/// <param name="SavingsPlanExpectation">Expectation for savings plans related to this account.</param>
public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string? Iban,
    decimal CurrentBalance,
    Guid BankContactId,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
