namespace FinanceManager.Domain.Postings;

public sealed class Posting : Entity, IAggregateRoot
{
    private Posting() { }
    public Posting(Guid sourceId, PostingKind kind, Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId, DateTime bookingDate, decimal amount)
    {
        SourceId = Guards.NotEmpty(sourceId, nameof(sourceId));
        Kind = kind;
        AccountId = accountId;
        ContactId = contactId;
        SavingsPlanId = savingsPlanId;
        SecurityId = securityId;
        BookingDate = bookingDate;
        Amount = amount;
    }
    public Guid SourceId { get; private set; }
    public PostingKind Kind { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public decimal Amount { get; private set; }
}