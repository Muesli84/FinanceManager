using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record AccountCreateRequest(
    [Required, MinLength(2)] string Name,
    AccountType Type,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);

public sealed record AccountUpdateRequest(
    [Required, MinLength(2)] string Name,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
