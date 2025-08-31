namespace FinanceManager.Domain.Statements;

public sealed class StatementEntry : Entity
{
    private StatementEntry() { }
    public StatementEntry(Guid statementImportId, DateTime bookingDate, decimal amount, string subject, string rawHash)
    {
        StatementImportId = Guards.NotEmpty(statementImportId, nameof(statementImportId));
        BookingDate = bookingDate;
        Amount = amount; // 0-Betrag erlaubt wenn Gebühren / Neutral; Validierung später
        Subject = Guards.NotNullOrWhiteSpace(subject, nameof(subject));
        RawHash = Guards.NotNullOrWhiteSpace(rawHash, nameof(rawHash));
        Status = StatementEntryStatus.Pending;
    }
    public Guid StatementImportId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Subject { get; private set; } = null!;
    public string RawHash { get; private set; } = null!;
    public Guid? ContactId { get; private set; }
    public Guid? SavingsPlanId { get; private set; }
    public Guid? SecurityTransactionId { get; private set; }
    public StatementEntryStatus Status { get; private set; } = StatementEntryStatus.Pending;
}