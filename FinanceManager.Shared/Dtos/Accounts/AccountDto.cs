namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// DTO representing a bank account and its core properties used by the client UI.
/// </summary>
/// <param name="Id">Identifier of the entity.</param>
/// <param name="Name">The name.</param>
/// <param name="Type">The type.</param>
/// <param name="Iban">The iban.</param>
/// <param name="CurrentBalance">The current balance.</param>
/// <param name="BankContactId">The bank contact id.</param>
/// <param name="SymbolAttachmentId">The symbol attachment id.</param>
/// <param name="SavingsPlanExpectation">The savings plan expectation.</param>
/// <param name="SecurityProcessingEnabled">The security processing enabled.</param>
/// <returns>The result.</returns>
public sealed record AccountDto(
    Guid Id,
    string Name,
    AccountType Type,
    string? Iban,
    decimal CurrentBalance,
    Guid BankContactId,
    Guid? SymbolAttachmentId,
    SavingsPlanExpectation SavingsPlanExpectation,
    bool SecurityProcessingEnabled)
{
    /// <summary>
    /// Indicates whether this account is a collection account grouping multiple sub-IBANs.
    /// </summary>
    public bool IsCollectionAccount { get; init; } = false;

    /// <summary>
    /// List of linked sub-IBANs; empty for non-collection accounts.
    /// </summary>
    /// <returns>The result.</returns>
    public IReadOnlyList<string> LinkedIbans { get; init; } = Array.Empty<string>();
}
