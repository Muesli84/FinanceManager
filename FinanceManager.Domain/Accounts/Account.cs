namespace FinanceManager.Domain.Accounts;

/// <summary>
/// Aggregate root representing a user bank account with balance and metadata.
/// Encapsulates invariants and operations related to an account.
/// </summary>
public sealed class Account : Entity, IAggregateRoot
{
    /// <summary>
    /// Creates a new account for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="type">Type of the account.</param>
    /// <param name="name">Display name of the account.</param>
    /// <param name="iban">Optional IBAN (may be null).</param>
    /// <param name="bankContactId">Identifier of the bank contact associated with this account.</param>
    public Account(Guid ownerUserId, AccountType type, string name, string? iban, Guid bankContactId)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Type = type;
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Iban = iban?.Trim();
        BankContactId = Guards.NotEmpty(bankContactId, nameof(bankContactId));
        SavingsPlanExpectation = SavingsPlanExpectation.Optional;
        SecurityProcessingEnabled = true;
    }

    /// <summary>
    /// Identifier of the user who owns this account.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Account type (e.g. Current, Savings).
    /// </summary>
    public AccountType Type { get; private set; }

    /// <summary>
    /// Display name of the account.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Optional IBAN of the account.
    /// </summary>
    public string? Iban { get; private set; }

    /// <summary>
    /// Current balance of the account.
    /// </summary>
    public decimal CurrentBalance { get; private set; }

    /// <summary>
    /// Bank contact identifier associated with the account.
    /// </summary>
    public Guid BankContactId { get; private set; }

    /// <summary>
    /// Optional symbol attachment id assigned to the account.
    /// </summary>
    public Guid? SymbolAttachmentId { get; private set; }

    /// <summary>
    /// Expected savings plan behavior attached to this account.
    /// </summary>
    public SavingsPlanExpectation SavingsPlanExpectation { get; private set; }

    /// <summary>
    /// Indicates whether this account allows security processing on statement entries.
    /// </summary>
    public bool SecurityProcessingEnabled { get; private set; }

    /// <summary>
    /// Indicates whether this account is a collection account grouping multiple sub-IBANs.
    /// </summary>
    public bool IsCollectionAccount { get; private set; }

    /// <summary>
    /// Renames the account.
    /// </summary>
    /// <param name="name">New display name.</param>
    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    /// <summary>
    /// Sets or clears the IBAN for the account.
    /// </summary>
    /// <param name="iban">New IBAN value or null to clear.</param>
    public void SetIban(string? iban)
    {
        Iban = string.IsNullOrWhiteSpace(iban) ? null : iban.Trim();
        Touch();
    }

    /// <summary>
    /// Sets the bank contact for this account.
    /// </summary>
    /// <param name="bankContactId">Bank contact identifier.</param>
    public void SetBankContact(Guid bankContactId)
    {
        BankContactId = Guards.NotEmpty(bankContactId, nameof(bankContactId));
        Touch();
    }

    /// <summary>
    /// Changes the account type.
    /// </summary>
    /// <param name="type">New account type.</param>
    public void SetType(AccountType type)
    {
        if (Type != type)
        {
            Type = type;
            Touch();
        }
    }

    /// <summary>
    /// Adjusts the current balance by the specified delta.
    /// </summary>
    /// <param name="delta">Amount to add (positive) or subtract (negative) from the balance.</param>
    public void AdjustBalance(decimal delta)
    {
        CurrentBalance += delta;
        Touch();
    }

    /// <summary>
    /// Sets or clears the symbol attachment for the account.
    /// </summary>
    /// <param name="attachmentId">Attachment id to set or null to clear.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }

    /// <summary>
    /// Sets the savings plan expectation for this account.
    /// </summary>
    /// <param name="expectation">Savings plan expectation value.</param>
    public void SetSavingsPlanExpectation(SavingsPlanExpectation expectation)
    {
        if (SavingsPlanExpectation != expectation)
        {
            SavingsPlanExpectation = expectation;
            Touch();
        }
    }

    /// <summary>
    /// Enables or disables security processing for statement entries on this account.
    /// </summary>
    /// <param name="enabled">True to allow security processing; otherwise false.</param>
    public void SetSecurityProcessingEnabled(bool enabled)
    {
        if (SecurityProcessingEnabled != enabled)
        {
            SecurityProcessingEnabled = enabled;
            Touch();
        }
    }

    /// <summary>
    /// Marks or unmarks this account as a collection account.
    /// </summary>
    /// <param name="value">True to mark as collection account; otherwise false.</param>
    public void SetIsCollectionAccount(bool value)
    {
        if (IsCollectionAccount != value)
        {
            IsCollectionAccount = value;
            Touch();
        }
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of an <see cref="Account"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Entity identifier.</param>
    /// <param name="OwnerUserId">Owner user identifier.</param>
    /// <param name="Type">Account type.</param>
    /// <param name="Name">Account display name.</param>
    /// <param name="Iban">Optional IBAN.</param>
    /// <param name="CurrentBalance">Current balance value.</param>
    /// <param name="BankContactId">Bank contact identifier.</param>
    /// <param name="SymbolAttachmentId">Optional symbol attachment id.</param>
    /// <param name="SavingsPlanExpectation">Configured savings plan expectation.</param>
    /// <param name="CreatedUtc">Entity creation timestamp UTC.</param>
    /// <param name="ModifiedUtc">Entity last modified timestamp UTC, if any.</param>
    /// <param name="SecurityProcessingEnabled">Indicates whether security processing is allowed.</param>
    /// <param name="IsCollectionAccount">Indicates whether this is a collection account.</param>
    /// <returns>The result.</returns>
    public sealed record AccountBackupDto(Guid Id, Guid OwnerUserId, AccountType Type, string Name, string? Iban, decimal CurrentBalance, Guid BankContactId, Guid? SymbolAttachmentId, SavingsPlanExpectation SavingsPlanExpectation, DateTime CreatedUtc, DateTime? ModifiedUtc, bool SecurityProcessingEnabled = true, bool IsCollectionAccount = false);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this account.
    /// </summary>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="Type">The type.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Iban">The iban.</param>
    /// <param name="CurrentBalance">The current balance.</param>
    /// <param name="BankContactId">The bank contact id.</param>
    /// <param name="SymbolAttachmentId">The symbol attachment id.</param>
    /// <param name="SavingsPlanExpectation">The savings plan expectation.</param>
    /// <param name="CreatedUtc">The created utc.</param>
    /// <param name="ModifiedUtc">The modified utc.</param>
    /// <param name="SecurityProcessingEnabled">The security processing enabled.</param>
    /// <param name="IsCollectionAccount">The is collection account.</param>
    /// <returns>A <see cref="AccountBackupDto"/> containing values needed to restore the account.</returns>
    public AccountBackupDto ToBackupDto() => new AccountBackupDto(Id, OwnerUserId, Type, Name, Iban, CurrentBalance, BankContactId, SymbolAttachmentId, SavingsPlanExpectation, CreatedUtc, ModifiedUtc, SecurityProcessingEnabled, IsCollectionAccount);

    /// <summary>
    /// Applies values from a backup DTO to this account instance.
    /// Uses the entity's setters to preserve domain invariants where applicable.
    /// </summary>
    /// <param name="dto">The backup DTO to apply to this entity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(AccountBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        SetType(dto.Type);
        Rename(dto.Name);
        SetIban(dto.Iban);
        CurrentBalance = dto.CurrentBalance;
        SetBankContact(dto.BankContactId);
        SetSymbolAttachment(dto.SymbolAttachmentId);
        SetSavingsPlanExpectation(dto.SavingsPlanExpectation);
        SetSecurityProcessingEnabled(dto.SecurityProcessingEnabled);
        SetIsCollectionAccount(dto.IsCollectionAccount);
        SetDates(dto.CreatedUtc, dto.ModifiedUtc);
    }
}