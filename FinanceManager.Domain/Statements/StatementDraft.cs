using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;

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
    public StatementDraft(Guid ownerUserId, string originalFileName, string? accountNumber, string? description)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        AccountName = accountNumber;
        Status = StatementDraftStatus.Draft;
        Description = description ?? Path.GetFileNameWithoutExtension(originalFileName);
    }
    public Guid OwnerUserId { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string? AccountName { get; set; }
    public string? Description { get; set; }
    public Guid? DetectedAccountId { get; private set; }
    public StatementDraftStatus Status { get; private set; }
    public ICollection<StatementDraftEntry> Entries => _entries;

    /// <summary>
    /// Gemeinsame Upload-Gruppen-ID aller StatementDrafts, die aus demselben Datei-Upload hervorgegangen sind.
    /// </summary>
    public Guid? UploadGroupId { get; private set; }

    public void SetUploadGroup(Guid uploadGroupId)
    {
        if (UploadGroupId == null)
        {
            UploadGroupId = uploadGroupId;
            Touch();
        }
    }

    public void SetDetectedAccount(Guid accountId) { DetectedAccountId = accountId; Touch(); }

    // Existing simple variant (backwards compatibility)
    public StatementDraftEntry AddEntry(DateTime bookingDate, decimal amount, string subject)
        => AddEntry(bookingDate, amount, subject, null, null, null, null, false, false);

    // Extended variant with additional data
    public StatementDraftEntry AddEntry(
        DateTime bookingDate,
        decimal amount,
        string subject,
        string? recipientName,
        DateTime? valutaDate,
        string? currencyCode,
        string? bookingDescription,
        bool isAnnounced,
        bool isCostNeutral = false)
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
            isCostNeutral,
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
        bool isCostNeutral,
        StatementDraftEntryStatus status)
    {
        DraftId = Guards.NotEmpty(draftId, nameof(draftId));
        BookingDate = bookingDate;
        Amount = amount;
        Subject = subject ?? string.Empty;
        RecipientName = recipientName;
        ValutaDate = valutaDate;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? "EUR" : currencyCode!; // default EUR
        BookingDescription = bookingDescription;
        IsAnnounced = isAnnounced;
        IsCostNeutral = isCostNeutral;
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
    public Guid? SavingsPlanId { get; private set; }
    public bool ArchiveSavingsPlanOnBooking { get; private set; }
    public bool IsCostNeutral { get; private set; } = false;
    public Guid? SplitDraftId { get; private set; }
    public Guid? SecurityId { get; private set; }
    public SecurityTransactionType? SecurityTransactionType { get; private set; }
    public decimal? SecurityQuantity { get; private set; }
    public decimal? SecurityFeeAmount { get; private set; }
    public decimal? SecurityTaxAmount { get; private set; }

    public void UpdateCore(DateTime bookingDate, DateTime? valutaDate, decimal amount, string subject, string? recipientName, string? currencyCode, string? bookingDescription)
    {
        BookingDate = bookingDate;
        ValutaDate = valutaDate;
        Amount = amount;
        Subject = subject ?? string.Empty;
        RecipientName = string.IsNullOrWhiteSpace(recipientName) ? null : recipientName!.Trim();
        if (!string.IsNullOrWhiteSpace(currencyCode)) { CurrencyCode = currencyCode!; }
        BookingDescription = string.IsNullOrWhiteSpace(bookingDescription) ? null : bookingDescription!.Trim();
        Touch();
    }
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
        Status = IsAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;
        MarkCostNeutral(false);
        Touch();
    }
    public void MarkCostNeutral(bool isCostNeutral)
    {
        IsCostNeutral = isCostNeutral;
    }

    public void AssignSavingsPlan(Guid? savingsPlanId) => SavingsPlanId = savingsPlanId;

    public void SetArchiveSavingsPlanOnBooking(bool archive)
    {
        ArchiveSavingsPlanOnBooking = archive;
        Touch();
    }

    public void AssignSplitDraft(Guid splitDraftId)
    {
        if (SplitDraftId != null)
        {
            throw new InvalidOperationException("Split draft already assigned.");
        }
        SplitDraftId = splitDraftId;
        Touch();
    }

    public void ClearSplitDraft()
    {
        if (SplitDraftId != null)
        {
            SplitDraftId = null;
            Touch();
        }
    }

    public void AssignContactWithoutAccounting(Guid contactId)
    {
        ContactId = contactId;
        // Keep existing status (stay Open/Announced) – do not mark accounted yet.
        Touch();
    }
    public void SetSecurity(Guid? securityId, SecurityTransactionType? txType, decimal? quantity, decimal? fee, decimal? tax)
    {
        SecurityId = securityId;
        SecurityTransactionType = securityId == null ? null : txType;
        SecurityQuantity = securityId == null ? null : quantity;
        SecurityFeeAmount = securityId == null ? null : fee;
        SecurityTaxAmount = securityId == null ? null : tax;
    }

    public void MarkNeedsCheck()
    {
        Status = StatementDraftEntryStatus.Open;
        Touch();
    }
}

