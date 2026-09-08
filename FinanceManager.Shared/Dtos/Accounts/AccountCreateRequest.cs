using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Request payload to create a new bank account.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
/// <param name="Iban">The iban.</param>
/// <param name="BankContactId">The bank contact id.</param>
/// <param name="NewBankContactName">The new bank contact name.</param>
/// <param name="SymbolAttachmentId">The symbol attachment id.</param>
/// <param name="SavingsPlanExpectation">The savings plan expectation.</param>
/// <param name="SecurityProcessingEnabled">The security processing enabled.</param>
/// <param name="IsCollectionAccount">The is collection account.</param>
/// <returns>The result.</returns>
public sealed record AccountCreateRequest(
    [Required, MinLength(2)] string Name,
    AccountType Type,
    string? Iban,
    Guid? BankContactId,
    string? NewBankContactName,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation,
    bool SecurityProcessingEnabled = true,
    bool IsCollectionAccount = false);
