namespace FinanceManager.Domain.Statements;

public sealed class StatementDraft : Entity, IAggregateRoot
{
    private readonly List<StatementDraftEntry> _entries = new();
    private StatementDraft() { }
    public StatementDraft(Guid ownerUserId, string originalFileName)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        Status = StatementDraftStatus.Draft;
    }
    public Guid OwnerUserId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public Guid? DetectedAccountId { get; private set; }
    public StatementDraftStatus Status { get; private set; }
    public ICollection<StatementDraftEntry> Entries => _entries;
    public void SetDetectedAccount(Guid accountId) { DetectedAccountId = accountId; Touch(); }
    public StatementDraftEntry AddEntry(DateTime bookingDate, decimal amount, string subject)
    {
        var entry = new StatementDraftEntry(Id, bookingDate, amount, subject);
        _entries.Add(entry); 
        Touch();
        return entry;
    }
    public void MarkCommitted() { Status = StatementDraftStatus.Committed; Touch(); }
    public void Expire() { Status = StatementDraftStatus.Expired; Touch(); }
}

public sealed class StatementDraftEntry : Entity
{
    private StatementDraftEntry() { }
    public StatementDraftEntry(Guid draftId, DateTime bookingDate, decimal amount, string subject)
    {
        DraftId = Guards.NotEmpty(draftId, nameof(draftId));
        BookingDate = bookingDate;
        Amount = amount;
        Subject = Guards.NotNullOrWhiteSpace(subject, nameof(subject));
    }
    public Guid DraftId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Subject { get; private set; } = null!;
}
