using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to create a new bank account.
/// </summary>
/// <param name="Name">Display name of the account.</param>
/// <param name="Type">Account type (e.g., Giro or Savings).</param>
/// <param name="Iban">Optional IBAN of the account.</param>
/// <param name="BankContactId">Existing bank contact id to associate, if any.</param>
/// <param name="NewBankContactName">Optional new bank contact name to create when no contact is selected.</param>
/// <param name="SymbolAttachmentId">Optional attachment id for the account symbol.</param>
/// <param name="SavingsPlanExpectation">Expectation for savings plans related to this account.</param>
public sealed record AccountCreateRequest(
    [Required, MinLength(2)] string Name,
    AccountType Type,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);

/// <summary>
/// Request payload to update an existing bank account.
/// </summary>
/// <param name="Name">Display name of the account.</param>
/// <param name="Type">Account type (e.g., Giro or Savings).</param>
/// <param name="Iban">Optional IBAN of the account.</param>
/// <param name="BankContactId">Existing bank contact id to associate, if any.</param>
/// <param name="NewBankContactName">Optional new bank contact name to create when no contact is selected.</param>
/// <param name="SymbolAttachmentId">Optional attachment id for the account symbol.</param>
/// <param name="SavingsPlanExpectation">Expectation for savings plans related to this account.</param>
/// <param name="Archived">When true, marks the account archived.</param>
public sealed record AccountUpdateRequest(
    [Required, MinLength(2)] string Name,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
