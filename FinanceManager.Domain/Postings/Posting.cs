using FinanceManager.Shared.Dtos;

namespace FinanceManager.Domain.Postings;



public sealed class Posting : Entity, IAggregateRoot
{
    private Posting() { }

    // Backwards-compatible constructors that default ValutaDate to BookingDate
    public Posting(Guid sourceId, PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, DateTime bookingDate, decimal amount)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, bookingDate, amount, null, null, null, null, null) { }

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

    // New constructor including ValutaDate
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
    }

    // Keep overload without quantity that forwards to valuta constructor (rare use)
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
        SecurityPostingSubType? securitySubType)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, valutaDate, amount, subject, recipientName, description, securitySubType, null) { }

    public Guid SourceId { get; private set; }
    public Guid GroupId { get; private set; }
    public PostingKind Kind { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public DateTime BookingDate { get; private set; }
    // New: valuta date
    public DateTime ValutaDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Subject { get; private set; }
    public string? RecipientName { get; private set; }
    public string? Description { get; private set; }
    public SecurityPostingSubType? SecuritySubType { get; private set; }

    // Neu: Menge (nur für Wertpapier-Postings belegt)
    public decimal? Quantity { get; private set; }

    // New: reference to parent posting (used for split/linked postings)
    public Guid? ParentId { get; private set; }

    public Posting SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty) { throw new ArgumentException("Group id must not be empty", nameof(groupId)); }
        if (GroupId == Guid.Empty)
        {
            GroupId = groupId;
        }
        return this;
    }

    public Posting SetParent(Guid parentId)
    {
        if (parentId == Guid.Empty) throw new ArgumentException("Parent id must not be empty", nameof(parentId));
        if (ParentId == null)
        {
            ParentId = parentId;
        }
        return this;
    }
}