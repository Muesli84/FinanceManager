namespace FinanceManager.Domain.Statements;

/// <summary>
/// Represents a single entry parsed from an imported statement. Contains booking information and optional
/// resolved references for accounting (contact, savings plan, security transaction).
/// </summary>
public sealed class StatementEntry : Entity
{
    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private StatementEntry() { }

    /// <summary>
    /// Creates a new <see cref="StatementEntry"/> for a given statement import.
    /// </summary>
    /// <param name="statementImportId">Identifier of the owning statement import. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="bookingDate">Booking date of the entry.</param>
    /// <param name="amount">Monetary amount. Zero is allowed for neutral items.</param>
    /// <param name="subject">Subject or short description. Must not be null or whitespace.</param>
    /// <param name="rawHash">A hash identifying the raw line content for deduplication. Must not be null or whitespace.</param>
    /// <param name="recipientName">Optional recipient name extracted from the source.</param>
    /// <param name="valutaDate">Optional valuta/date-of-value for the amount.</param>
    /// <param name="currencyCode">Optional currency code (defaults to "EUR" when null or whitespace).</param>
    /// <param name="bookingDescription">Optional longer booking description.</param>
    /// <param name="isAnnounced">Whether the entry was announced/pre-booked in the source system.</param>
    /// <param name="isCostNeutral">Whether the entry is cost-neutral and should not affect totals.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="statementImportId"/> is <see cref="Guid.Empty"/>, or when <paramref name="subject"/> or <paramref name="rawHash"/> are null/whitespace (via guards).</exception>
    public StatementEntry(Guid statementImportId, DateTime bookingDate, decimal amount, string subject, string rawHash, string? recipientName, DateTime? valutaDate, string? currencyCode, string? bookingDescription, bool isAnnounced, bool isCostNeutral)
    {
        StatementImportId = Guards.NotEmpty(statementImportId, nameof(statementImportId));
        BookingDate = bookingDate;
        Amount = amount; // 0 amount allowed for neutral items (fees, etc.)
        Subject = Guards.NotNullOrWhiteSpace(subject, nameof(subject));
        RawHash = Guards.NotNullOrWhiteSpace(rawHash, nameof(rawHash));
        RecipientName = recipientName;
        ValutaDate = valutaDate;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "EUR" : currencyCode!;
        BookingDescription = bookingDescription;
        IsAnnounced = isAnnounced;
        IsCostNeutral = isCostNeutral;
        Status = StatementEntryStatus.Pending;
    }

    /// <summary>
    /// Identifier of the owning statement import.
    /// </summary>
    /// <value>The import GUID.</value>
    public Guid StatementImportId { get; private set; }

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
    /// Raw content hash used for deduplication of imported lines.
    /// </summary>
    public string RawHash { get; private set; } = null!;

    /// <summary>
    /// Optional recipient name extracted from the statement.
    /// </summary>
    public string? RecipientName { get; private set; }

    /// <summary>
    /// Currency code for the amount (ISO code). Default is "EUR".
    /// </summary>
    public string CurrencyCode { get; private set; } = "EUR";

    /// <summary>
    /// Optional longer booking description.
    /// </summary>
    public string? BookingDescription { get; private set; }

    /// <summary>
    /// Whether the entry was announced/pre-booked in the source system.
    /// </summary>
    public bool IsAnnounced { get; private set; }

    /// <summary>
    /// Whether the entry is cost-neutral and should not affect totals.
    /// </summary>
    public bool IsCostNeutral { get; private set; }

    /// <summary>
    /// Optional resolved contact id when the entry is matched to a contact.
    /// </summary>
    public Guid? ContactId { get; private set; }

    /// <summary>
    /// Optional assigned savings plan id when the entry is matched to a savings plan.
    /// </summary>
    public Guid? SavingsPlanId { get; private set; }

    /// <summary>
    /// Optional security transaction id when the entry is matched to a security booking.
    /// </summary>
    public Guid? SecurityTransactionId { get; private set; }

    /// <summary>
    /// Processing status of this imported entry.
    /// </summary>
    public StatementEntryStatus Status { get; private set; } = StatementEntryStatus.Pending;

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="StatementEntry"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the statement entry entity.</param>
    /// <param name="StatementImportId">Identifier of the owning statement import.</param>
    /// <param name="BookingDate">Booking date of the entry.</param>
    /// <param name="ValutaDate">Optional valuta/date-of-value.</param>
    /// <param name="Amount">Monetary amount of the entry.</param>
    /// <param name="Subject">Short subject or description.</param>
    /// <param name="RawHash">Raw content hash used for deduplication.</param>
    /// <param name="RecipientName">Optional recipient name extracted from the statement.</param>
    /// <param name="CurrencyCode">Currency code for the amount (ISO).</param>
    /// <param name="BookingDescription">Optional longer booking description.</param>
    /// <param name="IsAnnounced">Whether the entry was announced/pre-booked in the source.</param>
    /// <param name="IsCostNeutral">Whether the entry is cost-neutral and should not affect totals.</param>
    /// <param name="ContactId">Optional resolved contact id when matched.</param>
    /// <param name="SavingsPlanId">Optional assigned savings plan id when matched.</param>
    /// <param name="SecurityTransactionId">Optional security transaction id when matched.</param>
    /// <param name="Status">Processing status of the imported entry.</param>
    /// <returns>The result.</returns>
    public sealed record StatementEntryBackupDto(Guid Id, Guid StatementImportId, DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string RawHash, string? RecipientName, string CurrencyCode, string? BookingDescription, bool IsAnnounced, bool IsCostNeutral, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityTransactionId, StatementEntryStatus Status);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this statement entry.
    /// </summary>
    /// <param name="StatementImportId">The statement import id.</param>
    /// <param name="BookingDate">The booking date.</param>
    /// <param name="ValutaDate">The valuta date.</param>
    /// <param name="Amount">The amount.</param>
    /// <param name="Subject">The subject.</param>
    /// <param name="RawHash">The raw hash.</param>
    /// <param name="RecipientName">The recipient name.</param>
    /// <param name="CurrencyCode">The currency code.</param>
    /// <param name="BookingDescription">The booking description.</param>
    /// <param name="IsAnnounced">The is announced.</param>
    /// <param name="IsCostNeutral">The is cost neutral.</param>
    /// <param name="ContactId">The contact id.</param>
    /// <param name="SavingsPlanId">The savings plan id.</param>
    /// <param name="SecurityTransactionId">The security transaction id.</param>
    /// <param name="Status">The status.</param>
    /// <returns>A <see cref="StatementEntryBackupDto"/> containing the entry state suitable for backup/restore.</returns>
    public StatementEntryBackupDto ToBackupDto() => new StatementEntryBackupDto(Id, StatementImportId, BookingDate, ValutaDate, Amount, Subject, RawHash, RecipientName, CurrencyCode, BookingDescription, IsAnnounced, IsCostNeutral, ContactId, SavingsPlanId, SecurityTransactionId, Status);

    /// <summary>
    /// Applies values from the provided backup DTO to this <see cref="StatementEntry"/> instance.
    /// </summary>
    /// <param name="dto">The <see cref="StatementEntryBackupDto"/> containing values to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(StatementEntryBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        StatementImportId = dto.StatementImportId;
        BookingDate = dto.BookingDate;
        ValutaDate = dto.ValutaDate;
        Amount = dto.Amount;
        Subject = dto.Subject;
        RawHash = dto.RawHash;
        RecipientName = dto.RecipientName;
        CurrencyCode = dto.CurrencyCode;
        BookingDescription = dto.BookingDescription;
        IsAnnounced = dto.IsAnnounced;
        IsCostNeutral = dto.IsCostNeutral;
        ContactId = dto.ContactId;
        SavingsPlanId = dto.SavingsPlanId;
        SecurityTransactionId = dto.SecurityTransactionId;
        Status = dto.Status;
    }
}