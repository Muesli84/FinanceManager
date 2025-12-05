namespace FinanceManager.Shared.Dtos.Accounts;

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
