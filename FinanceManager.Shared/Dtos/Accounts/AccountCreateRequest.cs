using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Request payload to create a new bank account.
/// </summary>
public sealed record AccountCreateRequest(
    [Required, MinLength(2)] string Name,
    AccountType Type,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
