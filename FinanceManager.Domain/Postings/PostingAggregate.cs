namespace FinanceManager.Domain.Postings;

using FinanceManager.Shared.Dtos; // for SecurityPostingSubType

public enum AggregatePeriod
{
    Month = 0,
    Quarter = 1,
    HalfYear = 2,
    Year = 3
}

public enum AggregateDateKind
{
    Booking = 0,
    Valuta = 1
}

public sealed class PostingAggregate : Entity, IAggregateRoot
{
    private PostingAggregate() { }

    public PostingAggregate(
        PostingKind kind,
        Guid? accountId,
        Guid? contactId,
        Guid? savingsPlanId,
        Guid? securityId,
        DateTime periodStart,
        AggregatePeriod period,
        SecurityPostingSubType? securitySubType = null,
        AggregateDateKind dateKind = AggregateDateKind.Booking)
    {
        Kind = kind;
        AccountId = accountId;
        ContactId = contactId;
        SavingsPlanId = savingsPlanId;
        SecurityId = securityId;
        SecuritySubType = securitySubType;
        PeriodStart = periodStart.Date;
        Period = period;
        DateKind = dateKind;
        Amount = 0m;
    }

    public PostingKind Kind { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public SecurityPostingSubType? SecuritySubType { get; private set; }
    // new: which date was used to compute the aggregate (Booking or Valuta)
    public AggregateDateKind DateKind { get; private set; }
    public DateTime PeriodStart { get; private set; }
    public AggregatePeriod Period { get; private set; }
    public decimal Amount { get; private set; }

    public void Add(decimal delta)
    {
        if (delta == 0m) return;
        Amount += delta;
        Touch();
    }
}
