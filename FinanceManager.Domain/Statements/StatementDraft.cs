using FinanceManager.Domain.Securities;

namespace FinanceManager.Domain.Statements;



/// <summary>
/// Represents a draft of a statement file uploaded by a user. Contains metadata and a collection of draft entries
/// derived from the statement for later processing or manual matching.
/// </summary>
public sealed class StatementDraft : Entity, IAggregateRoot
{
    private readonly List<StatementDraftEntry> _entries = new();

    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private StatementDraft() { }

    /// <summary>
    /// Creates a new <see cref="StatementDraft"/> instance for the specified user and original filename.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier. Must not be an empty GUID.</param>
    /// <param name="originalFileName">Original uploaded file name. Must not be null or whitespace.</param>
    /// <param name="accountNumber">Optional account name or number detected from the file.</param>
    /// <param name="description">Optional description; when null the file name (without extension) is used.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ownerUserId"/> is empty or <paramref name="originalFileName"/> is null/whitespace (via guards).</exception>
    public StatementDraft(Guid ownerUserId, string originalFileName, string? accountNumber, string? description)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        AccountName = accountNumber;
        Status = StatementDraftStatus.Draft;
        IsPreliminary = false;
        Description = description ?? Path.GetFileNameWithoutExtension(originalFileName);
    }

    /// <summary>
    /// Creates a new <see cref="StatementDraft"/> with an explicit initial <paramref name="status"/>.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="originalFileName">Original uploaded file name.</param>
    /// <param name="accountNumber">Optional account name or number.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="status">Initial draft status.</param>
    public StatementDraft(Guid ownerUserId, string originalFileName, string? accountNumber, string? description, StatementDraftStatus status)
        : this(ownerUserId, originalFileName, accountNumber, description)
    {
        Status = status;
    }

    /// <summary>
    /// Identifier of the user who owns this draft.
    /// </summary>
    /// <value>The owner user GUID.</value>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Original file name of the uploaded statement.
    /// </summary>
    /// <value>Original file name string.</value>
    public string OriginalFileName { get; private set; } = null!;

    /// <summary>
    /// Optional account name/number extracted from the statement file.
    /// </summary>
    /// <value>Account name or null.</value>
    public string? AccountName { get; set; }

    /// <summary>
    /// Optional description for the draft. Defaults to file name without extension when not provided.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional detected account id that was matched against the statement content.
    /// </summary>
    /// <value>Detected account GUID or null.</value>
    public Guid? DetectedAccountId { get; private set; }

    /// <summary>
    /// Current status of the draft (Draft, Committed, Expired, ...).
    /// </summary>
    public StatementDraftStatus Status { get; private set; }

    /// <summary>
    /// Indicates whether this draft is a preliminary (provisional) bookings draft.
    /// </summary>
    public bool IsPreliminary { get; private set; }

    /// <summary>
    /// Collection of entries parsed from the statement file.
    /// </summary>
    /// <value>Collection of <see cref="StatementDraftEntry"/> objects.</value>
    public ICollection<StatementDraftEntry> Entries => _entries;

    /// <summary>
    /// Upload group identifier shared by drafts that originate from the same file upload operation.
    /// </summary>
    /// <value>Upload group GUID or null.</value>
    public Guid? UploadGroupId { get; private set; }

    /// <summary>
    /// Sets the upload group id if not already set.
    /// </summary>
    /// <param name="uploadGroupId">The upload group identifier to set.</param>
    public void SetUploadGroup(Guid uploadGroupId)
    {
        if (UploadGroupId == null)
        {
            UploadGroupId = uploadGroupId;
            Touch();
        }
    }

    /// <summary>
    /// Sets the detected account id for this draft.
    /// </summary>
    /// <param name="accountId">Detected account GUID.</param>
    public void SetDetectedAccount(Guid accountId)
    {
        DetectedAccountId = accountId;
        Touch();
    }

    /// <summary>
    /// Adds a simple entry to the draft using required fields. This is a convenience overload.
    /// </summary>
    /// <param name="bookingDate">Booking date of the entry.</param>
    /// <param name="amount">Monetary amount.</param>
    /// <param name="subject">Subject/description text.</param>
    /// <returns>The created <see cref="StatementDraftEntry"/>.</returns>
    public StatementDraftEntry AddEntry(DateTime bookingDate, decimal amount, string subject)
        => AddEntry(bookingDate, amount, subject, null, null, null, null, false, false);

    /// <summary>
    /// Adds an entry to the draft with extended metadata.
    /// </summary>
    /// <param name="bookingDate">Booking date of the entry.</param>
    /// <param name="amount">Monetary amount of the entry.</param>
    /// <param name="subject">Subject/description text.</param>
    /// <param name="recipientName">Optional recipient name.</param>
    /// <param name="valutaDate">Optional valuta/date-of-value.</param>
    /// <param name="currencyCode">Optional currency code (defaults to EUR when null).</param>
    /// <param name="bookingDescription">Optional booking description.</param>
    /// <param name="isAnnounced">Whether the entry was announced (pre-booked) in the source system.</param>
    /// <param name="isCostNeutral">Whether the entry is cost-neutral and should not affect totals.</param>
    /// <returns>The created <see cref="StatementDraftEntry"/> instance.</returns>
    public StatementDraftEntry AddEntry(
        DateTime bookingDate,
        decimal amount,
        string subject,
        string? recipientName,
        DateTime? valutaDate,
        string? currencyCode,
        string? bookingDescription,
        bool isAnnounced,
        bool isCostNeutral = false)
    {
        var status = isAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        var entryNo = _entries.Any() ? _entries.Max(e => e.EntryNumber) : 0;
        var entry = new StatementDraftEntry(
            entryNo + 1,
            Id,
            bookingDate,
            amount,
            subject,
            recipientName,
            valutaDate,
            currencyCode,
            bookingDescription,
            isAnnounced,
            isCostNeutral,
            status);
        _entries.Add(entry);
        Touch();
        return entry;
    }

    /// <summary>
    /// Marks the draft as committed (ready to be applied to account postings).
    /// </summary>
    public void MarkCommitted()
    {
        Status = StatementDraftStatus.Committed;
        Touch();
    }

    /// <summary>
    /// Expires the draft and sets its status to Expired.
    /// </summary>
    public void Expire()
    {
        Status = StatementDraftStatus.Expired;
        Touch();
    }

    /// <summary>
    /// Marks this draft as a preliminary (provisional) bookings draft.
    /// </summary>
    public void MarkAsPreliminary()
    {
        IsPreliminary = true;
        Touch();
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="StatementDraft"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the draft entity.</param>
    /// <param name="OwnerUserId">Identifier of the user who owns the draft.</param>
    /// <param name="OriginalFileName">Original uploaded file name.</param>
    /// <param name="AccountName">Optional account name/number extracted from the file.</param>
    /// <param name="Description">Optional human description for the draft.</param>
    /// <param name="DetectedAccountId">Optional detected account identifier.</param>
    /// <param name="Status">Current status of the draft.</param>
    /// <param name="UploadGroupId">Optional upload group id shared by related drafts.</param>
    /// <param name="CreatedUtc">Creation timestamp in UTC.</param>
    /// <param name="ModifiedUtc">Last modification timestamp in UTC, if any.</param>
    /// <param name="Entries">List of contained draft entries as backup DTOs.</param>
    /// <param name="IsPreliminary">Indicates whether the draft is a preliminary (provisional) booking.</param>
    /// <returns>The result.</returns>
    public sealed record StatementDraftBackupDto(Guid Id, Guid OwnerUserId, string OriginalFileName, string? AccountName, string? Description, Guid? DetectedAccountId, StatementDraftStatus Status, Guid? UploadGroupId, DateTime CreatedUtc, DateTime? ModifiedUtc, List<StatementDraftEntry.StatementDraftEntryBackupDto> Entries, bool IsPreliminary = false);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this draft and its entries.
    /// </summary>
    /// <returns>A <see cref="StatementDraftBackupDto"/> containing the draft metadata and entry DTOs.</returns>
    public StatementDraftBackupDto ToBackupDto()
        => new StatementDraftBackupDto(Id, OwnerUserId, OriginalFileName, AccountName, Description, DetectedAccountId, Status, UploadGroupId, CreatedUtc, ModifiedUtc, _entries.Select(e => e.ToBackupDto()).ToList(), IsPreliminary);

    /// <summary>
    /// Assigns values from a backup DTO to this draft instance. Existing entries are cleared and replaced by DTO contents.
    /// </summary>
    /// <param name="dto">The <see cref="StatementDraftBackupDto"/> to apply.</param>
    /// <param name="includeEntries">When true, entries from the DTO are also applied; when false, only draft metadata is updated.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(StatementDraftBackupDto dto, bool includeEntries)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        OriginalFileName = dto.OriginalFileName;
        AccountName = dto.AccountName;
        Description = dto.Description;
        DetectedAccountId = dto.DetectedAccountId;
        Status = dto.Status;
        IsPreliminary = dto.IsPreliminary;
        UploadGroupId = dto.UploadGroupId;
        SetDates(dto.CreatedUtc, dto.ModifiedUtc);
        if (includeEntries)
        {
            _entries.Clear();
            foreach (var e in dto.Entries)
            {
                var entry = new StatementDraftEntry();
                entry.AssignBackupDto(e);
                _entries.Add(entry);
            }
        }
    }
}

/// <summary>
/// Represents a single parsed entry from a statement draft. Contains matching and accounting metadata used during processing.
/// </summary>
public sealed class StatementDraftEntry : Entity
{
    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    internal StatementDraftEntry() { }

    /// <summary>
    /// Creates a new <see cref="StatementDraftEntry"/> associated with a draft.
    /// </summary>
    /// <param name="entryNo">Sequential entry number within the draft.</param>
    /// <param name="draftId">Identifier of the owning draft. Must not be empty.</param>
    /// <param name="bookingDate">Booking date of the entry.</param>
    /// <param name="amount">Monetary amount of the entry.</param>
    /// <param name="subject">Subject or short description.</param>
    /// <param name="recipientName">Optional recipient name.</param>
    /// <param name="valutaDate">Optional valuta date.</param>
    /// <param name="currencyCode">Currency code (defaults to "EUR" when null or whitespace).</param>
    /// <param name="bookingDescription">Optional longer booking description.</param>
    /// <param name="isAnnounced">Whether the entry was announced (pre-booked).</param>
    /// <param name="isCostNeutral">Whether the entry is cost-neutral.</param>
    /// <param name="status">Initial processing status for the entry.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="draftId"/> is empty (via guards).</exception>
    public StatementDraftEntry(
        int entryNo,
        Guid draftId,
        DateTime bookingDate,
        decimal amount,
        string subject,
        string? recipientName,
        DateTime? valutaDate,
        string? currencyCode,
        string? bookingDescription,
        bool isAnnounced,
        bool isCostNeutral,
        StatementDraftEntryStatus status)
    {
        EntryNumber = entryNo;
        DraftId = Guards.NotEmpty(draftId, nameof(draftId));
        BookingDate = bookingDate;
        Amount = amount;
        Subject = subject ?? string.Empty;
        RecipientName = recipientName;
        ValutaDate = valutaDate;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "EUR" : currencyCode!; // default EUR
        BookingDescription = bookingDescription;
        IsAnnounced = isAnnounced;
        IsCostNeutral = isCostNeutral;
        Status = status;
    }

    /// <summary>
    /// Draft identifier that owns this entry.
    /// </summary>
    public Guid DraftId { get; private set; }

    /// <summary>
    /// Gets the unique number assigned to this entry.
    /// </summary>
    public int EntryNumber { get; private set; }

    /// <summary>
    /// Booking date of the entry.
    /// </summary>
    public DateTime BookingDate { get; private set; }

    /// <summary>
    /// Optional valuta/date-of-value for the entry.
    /// </summary>
    public DateTime? ValutaDate { get; private set; }

    /// <summary>
    /// Monetary amount of the entry.
    /// </summary>
    public decimal Amount { get; private set; }

    /// <summary>
    /// Short subject or description.
    /// </summary>
    public string Subject { get; private set; } = null!;

    /// <summary>
    /// Optional recipient name extracted from the statement.
    /// </summary>
    public string? RecipientName { get; private set; }

    /// <summary>
    /// Currency code for the amount (ISO code). Default is "EUR".
    /// </summary>
    public string CurrencyCode { get; private set; } = "EUR";

    /// <summary>
    /// Optional booking description (longer textual information).
    /// </summary>
    public string? BookingDescription { get; private set; }

    /// <summary>
    /// Whether the entry was marked as announced/pre-booked in the source.
    /// </summary>
    public bool IsAnnounced { get; private set; }

    /// <summary>
    /// Processing status of the draft entry.
    /// </summary>
    public StatementDraftEntryStatus Status { get; private set; }

    /// <summary>
    /// Optional resolved contact id when the entry is accounted to a contact.
    /// </summary>
    public Guid? ContactId { get; private set; }

    /// <summary>
    /// Optional assigned savings plan id.
    /// </summary>
    public Guid? SavingsPlanId { get; private set; }

    /// <summary>
    /// When true, the assigned savings plan will be archived upon booking.
    /// </summary>
    public bool ArchiveSavingsPlanOnBooking { get; private set; }

    /// <summary>
    /// Whether the entry is cost-neutral and should not affect totals.
    /// </summary>
    public bool IsCostNeutral { get; private set; } = false;

    /// <summary>
    /// Optional split draft id when the entry was split into multiple drafts.
    /// </summary>
    public Guid? SplitDraftId { get; private set; }

    /// <summary>
    /// Optional assigned security id for securities-related entries.
    /// </summary>
    public Guid? SecurityId { get; private set; }

    /// <summary>
    /// Optional security transaction type for securities bookings.
    /// </summary>
    public SecurityTransactionType? SecurityTransactionType { get; private set; }

    /// <summary>
    /// Optional quantity for security transactions.
    /// </summary>
    public decimal? SecurityQuantity { get; private set; }

    /// <summary>
    /// Optional fee amount related to a security transaction.
    /// </summary>
    public decimal? SecurityFeeAmount { get; private set; }

    /// <summary>
    /// Optional tax amount related to a security transaction.
    /// </summary>
    public decimal? SecurityTaxAmount { get; private set; }


    /// <summary>
    /// Updates core fields of the draft entry.
    /// </summary>
    /// <param name="bookingDate">New booking date.</param>
    /// <param name="valutaDate">New valuta date.</param>
    /// <param name="amount">New amount.</param>
    /// <param name="subject">New subject text.</param>
    /// <param name="recipientName">New recipient name.</param>
    /// <param name="currencyCode">Currency code (when null/whitespace the existing value is preserved).</param>
    /// <param name="bookingDescription">Booking description (null/whitespace clears the value).</param>
    public void UpdateCore(DateTime bookingDate, DateTime? valutaDate, decimal amount, string subject, string? recipientName, string? currencyCode, string? bookingDescription)
    {
        BookingDate = bookingDate;
        ValutaDate = valutaDate;
        Amount = amount;
        Subject = subject ?? string.Empty;
        RecipientName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName!.Trim();
        if (!string.IsNullOrWhiteSpace(currencyCode)) { CurrencyCode = currencyCode!; }
        BookingDescription = string.IsNullOrWhiteSpace(bookingDescription) ? null : bookingDescription!.Trim();
        Touch();
    }

    /// <summary>
    /// Marks the entry as already booked (duplicate or already applied in the system).
    /// </summary>
    public void MarkAlreadyBooked()
    {
        Status = StatementDraftEntryStatus.AlreadyBooked;
        Touch();
    }

    /// <summary>
    /// Marks the entry as accounted and assigns the contact id.
    /// </summary>
    /// <param name="contactId">Contact id that the entry was accounted to.</param>
    public void MarkAccounted(Guid contactId)
    {
        ContactId = contactId;
        Status = StatementDraftEntryStatus.Accounted;
        Touch();
    }

    /// <summary>
    /// Clears the resolved contact assignment and resets status to Open or Announced depending on <see cref="IsAnnounced"/>.
    /// </summary>
    public void ClearContact()
    {
        ContactId = null;
        if (Status != StatementDraftEntryStatus.AlreadyBooked)
        {
            Status = IsAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        }
        Touch();
    }

    /// <summary>
    /// Resets the entry to open processing state and clears cost-neutral flag.
    /// </summary>
    public void ResetOpen()
    {
        Status = IsAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        MarkCostNeutral(false);
        Touch();
    }

    /// <summary>
    /// Sets or clears the cost-neutral flag for the entry.
    /// </summary>
    /// <param name="isCostNeutral">True to mark cost-neutral; false otherwise.</param>
    public void MarkCostNeutral(bool isCostNeutral)
    {
        IsCostNeutral = isCostNeutral;
    }

    /// <summary>
    /// Assigns a savings plan id to the entry.
    /// </summary>
    /// <param name="savingsPlanId">Savings plan GUID or null to clear.</param>
    public void AssignSavingsPlan(Guid? savingsPlanId) => SavingsPlanId = savingsPlanId;

    /// <summary>
    /// Sets whether the assigned savings plan should be archived when the entry is booked.
    /// </summary>
    /// <param name="archive">True to archive on booking; false to leave active.</param>
    public void SetArchiveSavingsPlanOnBooking(bool archive)
    {
        ArchiveSavingsPlanOnBooking = archive;
        Touch();
    }

    /// <summary>
    /// Assigns a split draft id to this entry. Throws when a split draft is already assigned.
    /// </summary>
    /// <param name="splitDraftId">Split draft GUID to assign.</param>
    /// <exception cref="InvalidOperationException">Thrown when a split draft is already assigned.</exception>
    public void AssignSplitDraft(Guid splitDraftId)
    {
        if (SplitDraftId != null)
        {
            throw new InvalidOperationException("Split draft already assigned.");
        }
        SplitDraftId = splitDraftId;
        Touch();
    }

    /// <summary>
    /// Clears an assigned split draft id if present.
    /// </summary>
    public void ClearSplitDraft()
    {
        if (SplitDraftId != null)
        {
            SplitDraftId = null;
            Touch();
        }
    }

    /// <summary>
    /// Assigns a contact id to the entry without marking it as accounted.
    /// </summary>
    /// <param name="contactId">Contact GUID to assign.</param>
    public void AssignContactWithoutAccounting(Guid contactId)
    {
        ContactId = contactId;
        // Keep existing status (stay Open/Announced) � do not mark accounted yet.
        Touch();
    }

    /// <summary>
    /// Assigns security-related data to the entry. When <paramref name="securityId"/> is null, all security fields are cleared.
    /// </summary>
    /// <param name="securityId">Security GUID to assign, or null to clear security data.</param>
    /// <param name="txType">Transaction type for the security booking.</param>
    /// <param name="quantity">Quantity of the security.</param>
    /// <param name="fee">Fee amount related to the security booking.</param>
    /// <param name="tax">Tax amount related to the security booking.</param>
    public void SetSecurity(Guid? securityId, SecurityTransactionType? txType, decimal? quantity, decimal? fee, decimal? tax)
    {
        SecurityId = securityId;
        SecurityTransactionType = securityId == null ? null : txType;
        SecurityQuantity = securityId == null ? null : quantity;
        SecurityFeeAmount = securityId == null ? null : fee;
        SecurityTaxAmount = securityId == null ? null : tax;
    }

    /// <summary>
    /// Overrides the valuta date for this entry.
    /// </summary>
    /// <param name="valutaDate">New valuta date or null to clear.</param>
    public void OverrideValutaDate(DateTime? valutaDate)
    {
        ValutaDate = valutaDate;
        Touch();
    }

    /// <summary>
    /// Marks the entry for manual checking by setting its status to Open.
    /// </summary>
    public void MarkNeedsCheck()
    {
        Status = StatementDraftEntryStatus.Open;
        Touch();
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="StatementDraftEntry"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Entry identifier.</param>
    /// <param name="DraftId">Identifier of the owning draft.</param>
    /// <param name="CreatedUtc">Creation timestamp in UTC.</param>
    /// <param name="ModifiedUtc">Last modification timestamp in UTC, if any.</param>
    /// <param name="BookingDate">Booking date of the entry.</param>
    /// <param name="ValutaDate">Optional valuta/date-of-value.</param>
    /// <param name="Amount">Monetary amount.</param>
    /// <param name="Subject">Entry subject.</param>
    /// <param name="RecipientName">Optional recipient name.</param>
    /// <param name="CurrencyCode">Currency code (ISO).</param>
    /// <param name="BookingDescription">Optional longer booking description.</param>
    /// <param name="IsAnnounced">Whether entry was announced in source.</param>
    /// <param name="IsCostNeutral">Whether entry is cost-neutral.</param>
    /// <param name="Status">Processing status of the draft entry.</param>
    /// <param name="ContactId">Optional resolved contact id.</param>
    /// <param name="SavingsPlanId">Optional assigned savings plan id.</param>
    /// <param name="ArchiveSavingsPlanOnBooking">Whether to archive assigned savings plan on booking.</param>
    /// <param name="SplitDraftId">Optional split draft id.</param>
    /// <param name="SecurityId">Optional security id.</param>
    /// <param name="SecurityTransactionType">Optional security transaction type.</param>
    /// <param name="SecurityQuantity">Optional quantity for security transactions.</param>
    /// <param name="SecurityFeeAmount">Optional fee amount for security transaction.</param>
    /// <param name="SecurityTaxAmount">Optional tax amount for security transaction.</param>
    /// <returns>The result.</returns>
    public sealed record StatementDraftEntryBackupDto(Guid Id, Guid DraftId, DateTime CreatedUtc, DateTime? ModifiedUtc, DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string CurrencyCode, string? BookingDescription, bool IsAnnounced, bool IsCostNeutral, StatementDraftEntryStatus Status, Guid? ContactId, Guid? SavingsPlanId, bool ArchiveSavingsPlanOnBooking, Guid? SplitDraftId, Guid? SecurityId, SecurityTransactionType? SecurityTransactionType, decimal? SecurityQuantity, decimal? SecurityFeeAmount, decimal? SecurityTaxAmount);

    /// <summary>
    /// Creates a backup DTO representing this draft entry.
    /// </summary>
    /// <returns>A <see cref="StatementDraftEntryBackupDto"/> with the serializable state.</returns>
    public StatementDraftEntryBackupDto ToBackupDto()
        => new StatementDraftEntryBackupDto(Id, DraftId, CreatedUtc, ModifiedUtc, BookingDate, ValutaDate, Amount, Subject, RecipientName, CurrencyCode, BookingDescription, IsAnnounced, IsCostNeutral, Status, ContactId, SavingsPlanId, ArchiveSavingsPlanOnBooking, SplitDraftId, SecurityId, SecurityTransactionType, SecurityQuantity, SecurityFeeAmount, SecurityTaxAmount);

    /// <summary>
    /// Assigns values from the provided backup DTO to this draft entry instance.
    /// </summary>
    /// <param name="dto">The <see cref="StatementDraftEntryBackupDto"/> to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(StatementDraftEntryBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        DraftId = dto.DraftId;
        BookingDate = dto.BookingDate;
        ValutaDate = dto.ValutaDate;
        Amount = dto.Amount;
        Subject = dto.Subject;
        RecipientName = dto.RecipientName;
        CurrencyCode = dto.CurrencyCode;
        BookingDescription = dto.BookingDescription;
        IsAnnounced = dto.IsAnnounced;
        IsCostNeutral = dto.IsCostNeutral;
        Status = dto.Status;
        ContactId = dto.ContactId;
        SavingsPlanId = dto.SavingsPlanId;
        ArchiveSavingsPlanOnBooking = dto.ArchiveSavingsPlanOnBooking;
        SplitDraftId = dto.SplitDraftId;
        SecurityId = dto.SecurityId;
        SecurityTransactionType = dto.SecurityTransactionType;
        SecurityQuantity = dto.SecurityQuantity;
        SecurityFeeAmount = dto.SecurityFeeAmount;
        SecurityTaxAmount = dto.SecurityTaxAmount;
        SetDates(dto.CreatedUtc, dto.ModifiedUtc);
    }
}

