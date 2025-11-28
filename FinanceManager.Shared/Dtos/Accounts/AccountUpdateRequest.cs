using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Request payload to update an existing bank account.
/// </summary>
public sealed record AccountUpdateRequest(
    [Required, MinLength(2)] string Name,
    AccountType Type,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation,
    bool Archived);
