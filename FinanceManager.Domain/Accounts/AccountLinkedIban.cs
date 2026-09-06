namespace FinanceManager.Domain.Accounts;

/// <summary>
/// Represents a linked IBAN (sub-account IBAN) associated with a collection account.
/// </summary>
public sealed class AccountLinkedIban : Entity
{
    /// <summary>
    /// Creates a new linked IBAN entry for the specified collection account.
    /// </summary>
    /// <param name="accountId">Identifier of the collection account.</param>
    /// <param name="iban">The IBAN to link.</param>
    public AccountLinkedIban(Guid accountId, string iban)
    {
        AccountId = accountId;
        Iban = iban?.Trim() ?? throw new ArgumentNullException(nameof(iban));
    }

    /// <summary>
    /// Identifier of the collection account that owns this linked IBAN.
    /// </summary>
    public Guid AccountId { get; private set; }

    /// <summary>
    /// The linked IBAN value.
    /// </summary>
    public string Iban { get; private set; } = null!;

    /// <summary>
    /// Navigation property back to the owning account.
    /// </summary>
    public Account? Account { get; private set; }
}

/// <summary>
/// Backup DTO for <see cref="AccountLinkedIban"/>.
/// </summary>
/// <param name="Id">Entity identifier.</param>
/// <param name="AccountId">Identifier of the owning collection account.</param>
/// <param name="Iban">The linked IBAN value.</param>
/// <returns>The result.</returns>
public sealed record AccountLinkedIbanBackupDto(Guid Id, Guid AccountId, string Iban);
