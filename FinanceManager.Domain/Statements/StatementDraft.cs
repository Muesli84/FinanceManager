namespace FinanceManager.Domain.Statements;

public enum StatementDraftEntryStatus
{
    Open = 0,
    Announced = 1,
    Accounted = 2, // Kontakt zugeordnet
    AlreadyBooked = 3
}

public sealed class StatementDraft : Entity, IAggregateRoot
{
    private readonly List<StatementDraftEntry> _entries = new();
    private StatementDraft() { }
    public StatementDraft(Guid ownerUserId, string originalFileName, string? accountNumber)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        AccountName = accountNumber;
        Status = StatementDraftStatus.Draft;
    }
    public Guid OwnerUserId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string? AccountName { get; set; }
    public Guid? DetectedAccountId { get; private set; }
    public StatementDraftStatus Status { get; private set; }
    public ICollection<StatementDraftEntry> Entries => _entries;

    public void SetDetectedAccount(Guid accountId) { DetectedAccountId = accountId; Touch(); }

    // Existing simple variant (backwards compatibility)
    public StatementDraftEntry AddEntry(DateTime bookingDate, decimal amount, string subject)
        => AddEntry(bookingDate, amount, subject, null, null, null, null, false);

    // Extended variant with additional data
    public StatementDraftEntry AddEntry(
        DateTime bookingDate,
        decimal amount,
        string subject,
        string? recipientName,
        DateTime? valutaDate,
        string? currencyCode,
        string? bookingDescription,
        bool isAnnounced)
    {
        var status = isAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        var entry = new StatementDraftEntry(
            Id,
            bookingDate,
            amount,
            subject,
            recipientName,
            valutaDate,
            currencyCode,
            bookingDescription,
            isAnnounced,
            status);
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
    public StatementDraftEntry(
        Guid draftId,
        DateTime bookingDate,
        decimal amount,
        string subject,
        string? recipientName,
        DateTime? valutaDate,
        string? currencyCode,
        string? bookingDescription,
        bool isAnnounced,
        StatementDraftEntryStatus status)
    {
        DraftId = Guards.NotEmpty(draftId, nameof(draftId));
        BookingDate = bookingDate;
        Amount = amount;
        Subject = Guards.NotNullOrWhiteSpace(subject, nameof(subject));
        RecipientName = recipientName;
        ValutaDate = valutaDate;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "EUR" : currencyCode!; // default EUR
        BookingDescription = bookingDescription;
        IsAnnounced = isAnnounced;
        Status = status;
    }
    public Guid DraftId { get; private set; }
    public DateTime BookingDate { get; private set; }
    public DateTime? ValutaDate { get; private set; }
    public decimal Amount { get; private set; }
    public string Subject { get; private set; } = null!;
    public string? RecipientName { get; private set; }
    public string CurrencyCode { get; private set; } = "EUR";
    public string? BookingDescription { get; private set; }
    public bool IsAnnounced { get; private set; }
    public StatementDraftEntryStatus Status { get; private set; }
    public Guid? ContactId { get; private set; }

    public void MarkAlreadyBooked() { Status = StatementDraftEntryStatus.AlreadyBooked; Touch(); }
    public void MarkAccounted(Guid contactId)
    {
        ContactId = contactId;
        Status = StatementDraftEntryStatus.Accounted;
        Touch();
    }
    public void ClearContact()
    {
        ContactId = null;
        if (Status != StatementDraftEntryStatus.AlreadyBooked)
        {
            Status = IsAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        }
        Touch();
    }
    public void ResetOpen()
    {
        if (Status == StatementDraftEntryStatus.AlreadyBooked) return; // don't downgrade duplicates
        Status = IsAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        Touch();
    }
}
