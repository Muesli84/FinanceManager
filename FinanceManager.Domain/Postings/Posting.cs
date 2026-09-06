namespace FinanceManager.Domain.Postings;



/// <summary>
/// Domain entity representing a posting/transaction line that may reference accounts, contacts, savings plans or securities.
/// </summary>
public sealed class Posting : Entity, IAggregateRoot
{
    /// <summary>
    /// Creates a basic posting with required fields.
    /// </summary>
    /// <param name="sourceId">Source id (external system id or import id).</param>
    /// <param name="kind">Posting kind identifying the domain target.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="contactId">Optional contact id.</param>
    /// <param name="savingsPlanId">Optional savings plan id.</param>
    /// <param name="securityId">Optional security id.</param>
    /// <param name="bookingDate">Booking date of the posting.</param>
    /// <param name="amount">Monetary amount for the posting.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceId"/> is an empty GUID.</exception>
    public Posting(Guid sourceId, PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, DateTime bookingDate, decimal amount)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, bookingDate, amount, null, null, null, null, null) { }

    /// <summary>
    /// Creates a posting with detailed information including optional fields.
    /// </summary>
    /// <param name="sourceId">Source id (external system id or import id).</param>
    /// <param name="kind">Posting kind identifying the domain target.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="contactId">Optional contact id.</param>
    /// <param name="savingsPlanId">Optional savings plan id.</param>
    /// <param name="securityId">Optional security id.</param>
    /// <param name="bookingDate">Booking date of the posting.</param>
    /// <param name="amount">Monetary amount for the posting.</param>
    /// <param name="subject">Optional subject for the posting.</param>
    /// <param name="recipientName">Optional recipient name.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="securitySubType">Optional security subtype.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceId"/> is an empty GUID.</exception>
    public Posting(
        Guid sourceId,
        PostingKind kind,
        Guid? accountId,
        Guid? contactId,
        Guid? savingsPlanId,
        Guid? securityId,
        DateTime bookingDate,
        decimal amount,
        string? subject,
        string? recipientName,
        string? description,
        SecurityPostingSubType? securitySubType)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, bookingDate, amount, subject, recipientName, description, securitySubType, null) { }

    // Backwards-compatible overload including quantity but no valutaDate (defaults valuta to booking)
    /// <summary>
    /// Creates a posting with quantity information; valuta date defaults to booking date.
    /// </summary>
    /// <param name="sourceId">Source id (external system id or import id).</param>
    /// <param name="kind">Posting kind identifying the domain target.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="contactId">Optional contact id.</param>
    /// <param name="savingsPlanId">Optional savings plan id.</param>
    /// <param name="securityId">Optional security id.</param>
    /// <param name="bookingDate">Booking date of the posting.</param>
    /// <param name="amount">Monetary amount for the posting.</param>
    /// <param name="subject">Optional subject for the posting.</param>
    /// <param name="recipientName">Optional recipient name.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="securitySubType">Optional security subtype.</param>
    /// <param name="quantity">Optional quantity for securities postings.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceId"/> is an empty GUID.</exception>
    public Posting(
        Guid sourceId,
        PostingKind kind,
        Guid? accountId,
        Guid? contactId,
        Guid? savingsPlanId,
        Guid? securityId,
        DateTime bookingDate,
        decimal amount,
        string? subject,
        string? recipientName,
        string? description,
        SecurityPostingSubType? securitySubType,
        decimal? quantity)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, bookingDate, amount, subject, recipientName, description, securitySubType, quantity) { }

    /// <summary>
    /// Creates a posting including valuta date for precise financial tracking.
    /// </summary>
    /// <param name="sourceId">Source identifier (external/import id).</param>
    /// <param name="kind">Posting kind identifying the target domain.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="contactId">Optional contact id.</param>
    /// <param name="savingsPlanId">Optional savings plan id.</param>
    /// <param name="securityId">Optional security id.</param>
    /// <param name="bookingDate">Booking date of the posting.</param>
    /// <param name="valutaDate">Valuta date for the posting.</param>
    /// <param name="amount">Monetary amount for the posting.</param>
    /// <param name="subject">Optional subject for the posting.</param>
    /// <param name="recipientName">Optional recipient name.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="securitySubType">Optional security subtype.</param>
    /// <param name="quantity">Optional quantity.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceId"/> is an empty GUID.</exception>
    public Posting(
        Guid sourceId,
        PostingKind kind,
        Guid? accountId,
        Guid? contactId,
        Guid? savingsPlanId,
        Guid? securityId,
        DateTime bookingDate,
        DateTime valutaDate,
        decimal amount,
        string? subject,
        string? recipientName,
        string? description,
        SecurityPostingSubType? securitySubType,
        decimal? quantity)
    {
        SourceId = Guards.NotEmpty(sourceId, nameof(sourceId));
        Kind = kind;
        AccountId = accountId;
        ContactId = contactId;
        SavingsPlanId = savingsPlanId;
        SecurityId = securityId;
        BookingDate = bookingDate;
        ValutaDate = valutaDate;
        Amount = amount;
        Subject = subject;
        RecipientName = recipientName;
        Description = description;
        SecuritySubType = securitySubType;
        GroupId = Guid.Empty; // will be set via SetGroup
        Quantity = quantity;
        ParentId = null; // default
        OriginalAmount = null;
    }

    /// <summary>
    /// Creates a posting with explicit booking and valuta dates and optional subtype.
    /// </summary>
    /// <param name="sourceId">Source identifier (external/import id).</param>
    /// <param name="kind">Posting kind identifying the target domain.</param>
    /// <param name="accountId">Optional account id.</param>
    /// <param name="contactId">Optional contact id.</param>
    /// <param name="savingsPlanId">Optional savings plan id.</param>
    /// <param name="securityId">Optional security id.</param>
    /// <param name="bookingDate">Booking date of the posting.</param>
    /// <param name="valutaDate">Valuta/date-of-value for the posting.</param>
    /// <param name="amount">Monetary amount for the posting.</param>
    /// <param name="text1">Optional text field 1.</param>
    /// <param name="text2">Optional text field 2.</param>
    /// <param name="text3">Optional text field 3.</param>
    /// <param name="subType">Optional security posting sub-type.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourceId"/> is an empty GUID.</exception>
    public Posting(Guid sourceId, PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, DateTime bookingDate, DateTime valutaDate, decimal amount, string? text1, string? text2, string? text3, SecurityPostingSubType? subType)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, valutaDate, amount, text1, text2, text3, subType, null) { }

    /// <summary>
    /// Source identifier (often an external or import id) associated with the posting.
    /// </summary>
    /// <value>The source identifier.</value>
    public Guid SourceId { get; private set; }

    /// <summary>
    /// Group identifier for related postings.
    /// </summary>
    /// <value>The group identifier.</value>
    public Guid GroupId { get; private set; }

    /// <summary>
    /// Kind of posting indicating target domain.
    /// </summary>
    /// <value>The posting kind.</value>
    public PostingKind Kind { get; private set; }

    /// <summary>
    /// Optional account id referenced by the posting.
    /// </summary>
    /// <value>The account id or null.</value>
    public Guid? AccountId { get; private set; }

    /// <summary>
    /// Optional contact id referenced by the posting.
    /// </summary>
    /// <value>The contact id or null.</value>
    public Guid? ContactId { get; private set; }

    /// <summary>
    /// Optional savings plan id referenced by the posting.
    /// </summary>
    /// <value>The savings plan id or null.</value>
    public Guid? SavingsPlanId { get; private set; }

    /// <summary>
    /// Optional security id referenced by the posting.
    /// </summary>
    /// <value>The security id or null.</value>
    public Guid? SecurityId { get; private set; }
    /// <summary>
    /// Booking date of the posting.
    /// </summary>
    /// <value>The booking date.</value>
    public DateTime BookingDate { get; private set; }
    /// <summary>
    /// Valuta date of the posting, used for financial tracking.
    /// </summary>
    /// <value>The valuta date.</value>
    public DateTime ValutaDate { get; private set; }
    /// <summary>
    /// Monetary amount for the posting.
    /// </summary>
    /// <value>The monetary amount.</value>
    public decimal Amount { get; private set; }
    /// <summary>
    /// Optional original amount for postings that have been zeroed out during booking.
    /// </summary>
    public decimal? OriginalAmount { get; private set; }
    /// <summary>
    /// Optional subject for the posting.
    /// </summary>
    /// <value>The subject or null.</value>
    public string? Subject { get; private set; }
    /// <summary>
    /// Optional recipient name.
    /// </summary>
    /// <value>The recipient name or null.</value>
    public string? RecipientName { get; private set; }
    /// <summary>
    /// Optional description.
    /// </summary>
    /// <value>The description or null.</value>
    public string? Description { get; private set; }
    /// <summary>
    /// Optional security subtype.
    /// </summary>
    /// <value>The security posting subtype or null.</value>
    public SecurityPostingSubType? SecuritySubType { get; private set; }

    // Neu: Menge (nur f�r Wertpapier-Postings belegt)
    /// <summary>
    /// Optional quantity, primarily used for securities postings.
    /// </summary>
    /// <value>The quantity or null.</value>
    public decimal? Quantity { get; private set; }

    // New: reference to parent posting (used for split/linked postings)
    /// <summary>
    /// Optional reference to a parent posting, used for split or linked postings.
    /// </summary>
    /// <value>The parent posting id or null.</value>
    public Guid? ParentId { get; private set; }

    // New: optional link to counterpart posting for self-transfers
    /// <summary>
    /// Optional link to a counterpart posting, used for self-transfers.
    /// </summary>
    /// <value>The linked posting id or null.</value>
    public Guid? LinkedPostingId { get; private set; }

    // Reversal tracking fields
    /// <summary>
    /// Optional reference to the reversal posting that cancels this posting.
    /// Set when this posting is reversed.
    /// </summary>
    /// <value>The reversal posting id or null.</value>
    public Guid? ReversedByPostingId { get; private set; }

    /// <summary>
    /// Optional reference to the original posting that this posting reverses.
    /// Set when this posting is a reversal of another posting.
    /// </summary>
    /// <value>The original posting id or null.</value>
    public Guid? ReversalForPostingId { get; private set; }

    /// <summary>
    /// User ID who initiated the reversal.
    /// Only set when this posting has been reversed (ReversedByPostingId is not null).
    /// </summary>
    /// <value>The user id who reversed this posting or null.</value>
    public Guid? ReversedByUserId { get; private set; }

    /// <summary>
    /// Timestamp when the reversal was created.
    /// Only set when this posting has been reversed (ReversedByPostingId is not null).
    /// </summary>
    /// <value>The reversal timestamp or null.</value>
    public DateTime? ReversedAtUtc { get; private set; }

    /// <summary>
    /// Sets the group identifier for the posting.
    /// </summary>
    /// <param name="groupId">The group identifier.</param>
    /// <returns>The updated <see cref="Posting"/> instance (fluent API).</returns>
    /// <exception cref="ArgumentException">Thrown when the <paramref name="groupId"/> is an empty GUID.</exception>
    public Posting SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty) { throw new ArgumentException("Group id must not be empty", nameof(groupId)); }
        if (GroupId == Guid.Empty)
        {
            GroupId = groupId;
        }
        return this;
    }

    /// <summary>
    /// Sets the parent identifier for the posting.
    /// </summary>
    /// <param name="parentId">The parent identifier.</param>
    /// <returns>The updated <see cref="Posting"/> instance (fluent API).</returns>
    /// <exception cref="ArgumentException">Thrown when the <paramref name="parentId"/> is an empty GUID.</exception>
    public Posting SetParent(Guid parentId)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("Parent id must not be empty", nameof(parentId));
        if (ParentId == null)
        {
            ParentId = parentId;
        }
        return this;
    }

    /// <summary>
    /// Sets the linked posting identifier for self-transfer postings.
    /// </summary>
    /// <param name="linkedPostingId">The linked posting identifier.</param>
    /// <returns>The updated <see cref="Posting"/> instance (fluent API).</returns>
    /// <exception cref="ArgumentException">Thrown when the <paramref name="linkedPostingId"/> is an empty GUID.</exception>
    public Posting SetLinkedPosting(Guid linkedPostingId)
    {
        if (linkedPostingId == Guid.Empty) throw new ArgumentException("Linked posting id must not be empty", nameof(linkedPostingId));
        if (LinkedPostingId == null)
        {
            LinkedPostingId = linkedPostingId;
        }
        return this;
    }

    /// <summary>
    /// Sets the valuta date for the posting.
    /// </summary>
    /// <param name="valutaDate">The valuta date.</param>
    /// <returns>The updated <see cref="Posting"/> instance (fluent API).</returns>
    public Posting SetValutaDate(DateTime valutaDate)
    {
        ValutaDate = valutaDate;
        Touch();
        return this;
    }

    /// <summary>
    /// Marks this posting as reversed by a reversal posting.
    /// </summary>
    /// <param name="reversalPosting">The reversal posting that cancels this posting.</param>
    /// <param name="userId">The user ID who initiated the reversal.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reversalPosting"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when this posting is already reversed.</exception>
    public void SetReversedBy(Posting reversalPosting, Guid userId)
    {
        if (reversalPosting == null)
        {
            throw new ArgumentNullException(nameof(reversalPosting));
        }

        if (ReversedByPostingId.HasValue)
        {
            throw new InvalidOperationException($"Posting {Id} is already reversed by posting {ReversedByPostingId.Value}");
        }

        ReversedByPostingId = reversalPosting.Id;
        ReversedByUserId = userId;
        ReversedAtUtc = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Marks this posting as a reversal of an original posting.
    /// </summary>
    /// <param name="originalPosting">The original posting that this posting reverses.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="originalPosting"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when this posting is already marked as a reversal.</exception>
    public void SetReversalFor(Posting originalPosting)
    {
        if (originalPosting == null)
        {
            throw new ArgumentNullException(nameof(originalPosting));
        }

        if (ReversalForPostingId.HasValue)
        {
            throw new InvalidOperationException($"Posting {Id} is already a reversal for posting {ReversalForPostingId.Value}");
        }

        ReversalForPostingId = originalPosting.Id;
        Touch();
    }

    /// <summary>
    /// Gets a value indicating whether this posting has been reversed.
    /// </summary>
    /// <value><c>true</c> if this posting has been reversed; otherwise, <c>false</c>.</value>
    public bool IsReversed => ReversedByPostingId.HasValue;

    /// <summary>
    /// Gets a value indicating whether this posting is a reversal of another posting.
    /// </summary>
    /// <value><c>true</c> if this posting is a reversal; otherwise, <c>false</c>.</value>
    public bool IsReversal => ReversalForPostingId.HasValue;

    /// <summary>
    /// Gets or sets a value indicating whether this posting is a preliminary (provisional) booking.
    /// </summary>
    /// <value><c>true</c> if this posting is a preliminary booking; otherwise, <c>false</c>.</value>
    public bool IsPreliminary { get; private set; }

    /// <summary>
    /// Marks this posting as preliminary or clears the preliminary flag.
    /// </summary>
    /// <param name="isPreliminary"><c>true</c> to mark the posting as preliminary; otherwise, <c>false</c>.</param>
    /// <returns>This posting for fluent chaining.</returns>
    public Posting SetIsPreliminary(bool isPreliminary)
    {
        IsPreliminary = isPreliminary;
        Touch();
        return this;
    }

    /// <summary>
    /// Sets the original amount for informational purposes.
    /// </summary>
    /// <param name="amount">Original amount or null to clear.</param>
    /// <returns>The updated <see cref="Posting"/> instance (fluent API).</returns>
    public Posting SetOriginalAmount(decimal? amount)
    {
        OriginalAmount = amount;
        Touch();
        return this;
    }

    /// <summary>
    /// Zeros the amount while preserving the original amount for traceability.
    /// </summary>
    /// <param name="originalAmount">Optional original amount to store; uses the current amount if not supplied.</param>
    /// <returns>This posting for fluent chaining.</returns>
    public Posting ZeroAmount(decimal? originalAmount = null)
    {
        if (Amount != 0m)
        {
            OriginalAmount = originalAmount ?? Amount;
            Amount = 0m;
        }
        Touch();
        return this;
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="Posting"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the posting entity.</param>
    /// <param name="SourceId">Source identifier (external/import id).</param>
    /// <param name="Kind">Posting kind indicating the domain target.</param>
    /// <param name="AccountId">Optional account id referenced by the posting.</param>
    /// <param name="ContactId">Optional contact id referenced by the posting.</param>
    /// <param name="SavingsPlanId">Optional savings plan id referenced by the posting.</param>
    /// <param name="SecurityId">Optional security id referenced by the posting.</param>
    /// <param name="BookingDate">Booking date of the posting.</param>
    /// <param name="ValutaDate">Valuta/date-of-value for the posting.</param>
    /// <param name="Amount">Monetary amount for the posting.</param>
    /// <param name="OriginalAmount">Optional original amount before zeroing.</param>
    /// <param name="Subject">Optional subject for the posting.</param>
    /// <param name="RecipientName">Optional recipient name.</param>
    /// <param name="Description">Optional description.</param>
    /// <param name="SecuritySubType">Optional security posting subtype.</param>
    /// <param name="Quantity">Optional quantity for securities postings.</param>
    /// <param name="GroupId">Optional group identifier for related postings.</param>
    /// <param name="ParentId">Optional parent posting reference.</param>
    /// <param name="LinkedPostingId">Optional linked posting reference (counterpart).</param>
    /// <param name="ReversedByPostingId">Optional reference to the reversal posting.</param>
    /// <param name="ReversalForPostingId">Optional reference to the original posting.</param>
    /// <param name="ReversedByUserId">Optional user ID who reversed this posting.</param>
    /// <param name="ReversedAtUtc">Optional timestamp when this posting was reversed.</param>
    /// <param name="IsPreliminary">Indicates whether this posting is a preliminary (provisional) booking.</param>
    /// <returns>The result.</returns>
    public sealed record PostingBackupDto(Guid Id, Guid SourceId, PostingKind Kind, Guid? AccountId, Guid? ContactId, Guid? SavingsPlanId, Guid? SecurityId, DateTime BookingDate, DateTime ValutaDate, decimal Amount, decimal? OriginalAmount, string? Subject, string? RecipientName, string? Description, SecurityPostingSubType? SecuritySubType, decimal? Quantity, Guid? GroupId, Guid? ParentId, Guid? LinkedPostingId, Guid? ReversedByPostingId, Guid? ReversalForPostingId, Guid? ReversedByUserId, DateTime? ReversedAtUtc, bool IsPreliminary = false);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this posting.
    /// </summary>
    /// <param name="SourceId">The source id.</param>
    /// <param name="Kind">The kind.</param>
    /// <param name="AccountId">The account id.</param>
    /// <param name="ContactId">The contact id.</param>
    /// <param name="SavingsPlanId">The savings plan id.</param>
    /// <param name="SecurityId">The security id.</param>
    /// <param name="BookingDate">The booking date.</param>
    /// <param name="ValutaDate">The valuta date.</param>
    /// <param name="Amount">The amount.</param>
    /// <param name="OriginalAmount">The original amount.</param>
    /// <param name="Subject">The subject.</param>
    /// <param name="RecipientName">The recipient name.</param>
    /// <param name="Description">The description.</param>
    /// <param name="SecuritySubType">The security sub type.</param>
    /// <param name="Quantity">The quantity.</param>
    /// <param name="GroupId">The group id.</param>
    /// <param name="ParentId">The parent id.</param>
    /// <param name="LinkedPostingId">The linked posting id.</param>
    /// <param name="ReversedByPostingId">The reversed by posting id.</param>
    /// <param name="ReversalForPostingId">The reversal for posting id.</param>
    /// <param name="ReversedByUserId">The reversed by user id.</param>
    /// <param name="ReversedAtUtc">The reversed at utc.</param>
    /// <param name="IsPreliminary">The is preliminary.</param>
    /// <returns>A <see cref="PostingBackupDto"/> containing values required to restore this posting.</returns>
    public PostingBackupDto ToBackupDto() => new PostingBackupDto(Id, SourceId, Kind, AccountId, ContactId, SavingsPlanId, SecurityId, BookingDate, ValutaDate, Amount, OriginalAmount, Subject, RecipientName, Description, SecuritySubType, Quantity, GroupId, ParentId, LinkedPostingId, ReversedByPostingId, ReversalForPostingId, ReversedByUserId, ReversedAtUtc, IsPreliminary);

    /// <summary>
    /// Applies values from the provided backup DTO to this posting instance.
    /// </summary>
    /// <param name="dto">The <see cref="PostingBackupDto"/> containing values to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(PostingBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        SourceId = dto.SourceId;
        Kind = dto.Kind;
        AccountId = dto.AccountId;
        ContactId = dto.ContactId;
        SavingsPlanId = dto.SavingsPlanId;
        SecurityId = dto.SecurityId;
        BookingDate = dto.BookingDate;
        ValutaDate = dto.ValutaDate;
        Amount = dto.Amount;
        OriginalAmount = dto.OriginalAmount;
        Subject = dto.Subject;
        RecipientName = dto.RecipientName;
        Description = dto.Description;
        SecuritySubType = dto.SecuritySubType;
        Quantity = dto.Quantity;
        GroupId = dto.GroupId ?? Guid.Empty;
        ParentId = dto.ParentId;
        LinkedPostingId = dto.LinkedPostingId;
        ReversedByPostingId = dto.ReversedByPostingId;
        ReversalForPostingId = dto.ReversalForPostingId;
        ReversedByUserId = dto.ReversedByUserId;
        ReversedAtUtc = dto.ReversedAtUtc;
        IsPreliminary = dto.IsPreliminary;
    }
}