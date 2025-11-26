namespace FinanceManager.Shared.Dtos;

public enum AccountType
{
    Giro = 0,
    Savings = 1
}

public enum SavingsPlanExpectation : short
{
    None = 0,
    Optional = 1,
    Required = 2
}

public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string? Iban,
    decimal CurrentBalance,
    Guid BankContactId,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation);
