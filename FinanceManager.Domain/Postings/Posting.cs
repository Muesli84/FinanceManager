using FinanceManager.Shared.Dtos;

namespace FinanceManager.Domain.Postings;



public sealed class Posting : Entity, IAggregateRoot
{
    private Posting() { }

    public Posting(Guid sourceId, PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, DateTime bookingDate, decimal amount)
        : this(sourceId, kind, accountId, contactId, savingsPlanId, securityId, bookingDate, amount, null, null, null, null) { }

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
    {
        SourceId = Guards.NotEmpty(sourceId, nameof(sourceId));
        Kind = kind;
        AccountId = accountId;
        ContactId = contactId;
        SavingsPlanId = savingsPlanId;
        SecurityId = securityId;
        BookingDate = bookingDate;
        Amount = amount;
        Subject = subject;
        RecipientName = recipientName;
        Description = description;
        SecuritySubType = securitySubType;
        GroupId = Guid.Empty; // will be set via SetGroup
    }

    public Guid SourceId { get; private set; }
    public Guid GroupId { get; private set; }
    public PostingKind Kind { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public decimal Amount { get; private set; }
    public string? Subject { get; private set; }
    public string? RecipientName { get; private set; }
    public string? Description { get; private set; }
    public SecurityPostingSubType? SecuritySubType { get; private set; }

    public Posting SetGroup(Guid groupId)
    {
        if (groupId == Guid.Empty) { throw new ArgumentException("Group id must not be empty", nameof(groupId)); }
        if (GroupId == Guid.Empty)
        {
            GroupId = groupId;
        }
        return this;
    }
}