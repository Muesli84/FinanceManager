namespace FinanceManager.Domain.Statements;

public sealed class StatementEntry : Entity
{
    private StatementEntry() { }
    public StatementEntry(Guid statementImportId, DateTime bookingDate, decimal amount, string subject, string rawHash, string? recipientName, DateTime? valutaDate, string? currencyCode, string? bookingDescription, bool isAnnounced)
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
        Status = StatementEntryStatus.Pending;
    }
    public Guid StatementImportId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public DateTime? ValutaDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Subject { get; private set; } = null!;
    public string RawHash { get; private set; } = null!;
    public string? RecipientName { get; private set; }
    public string CurrencyCode { get; private set; } = "EUR";
    public string? BookingDescription { get; private set; }
    public bool IsAnnounced { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityTransactionId { get; private set; }
    public StatementEntryStatus Status { get; private set; } = StatementEntryStatus.Pending;
}