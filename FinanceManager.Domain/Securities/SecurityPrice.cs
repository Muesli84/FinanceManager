namespace FinanceManager.Domain.Securities;

public sealed class SecurityPrice
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid SecurityId { get; private set; }
    public DateTime Date { get; private set; }
    public decimal Close { get; private set; }
    public DateTime CreatedUtc { get; private set; } = DateTime.UtcNow;

    private SecurityPrice() { }

    public SecurityPrice(Guid securityId, DateTime date, decimal close)
    {
        if (securityId == Guid.Empty) throw new ArgumentException("SecurityId required", nameof(securityId));
        SecurityId = securityId;
        Date = date.Date;
        Close = close;
    }
}