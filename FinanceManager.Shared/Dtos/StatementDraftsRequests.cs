using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload describing a statement draft upload operation.
/// </summary>
public sealed record StatementDraftUploadRequest([property: Required] string FileName);

/// <summary>
/// Result returned after uploading a statement draft file.
/// </summary>
public sealed record StatementDraftUploadResult(StatementDraftDto? FirstDraft, object? SplitInfo);

/// <summary>
/// Options used to trigger mass booking of open statement drafts.
/// </summary>
public sealed record StatementDraftMassBookRequest(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);

/// <summary>
/// Request to assign or clear a split draft association on a statement draft entry.
/// </summary>
public sealed record StatementDraftSetSplitDraftRequest(Guid? SplitDraftId);

/// <summary>
/// Request to add a new entry to an existing statement draft.
/// </summary>
public sealed record StatementDraftAddEntryRequest([property: Required] DateTime BookingDate, [property: Required] decimal Amount, [property: Required, MaxLength(500)] string Subject);

/// <summary>
/// Request to commit an existing statement draft to postings.
/// </summary>
public sealed record StatementDraftCommitRequest(Guid AccountId, ImportFormat Format);

/// <summary>
/// Request to set or clear the contact association of a statement draft entry.
/// </summary>
public sealed record StatementDraftSetContactRequest(Guid? ContactId);

/// <summary>
/// Request to set or clear the cost-neutral flag of a statement draft entry.
/// </summary>
public sealed record StatementDraftSetCostNeutralRequest(bool? IsCostNeutral);

/// <summary>
/// Request to set or clear an associated savings plan for a statement draft entry.
/// </summary>
public sealed record StatementDraftSetSavingsPlanRequest(Guid? SavingsPlanId);

/// <summary>
/// Request to control whether a savings plan should be archived once booked.
/// </summary>
public sealed record StatementDraftSetArchiveSavingsPlanOnBookingRequest(bool ArchiveOnBooking);

/// <summary>
/// Request to edit the core fields of a statement draft entry.
/// </summary>
public sealed record StatementDraftUpdateEntryCoreRequest(DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string? CurrencyCode, string? BookingDescription);

/// <summary>
/// Request to assign security details to a statement draft entry (trade, dividend, fees, taxes).
/// </summary>
public sealed record StatementDraftSetEntrySecurityRequest(Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);

/// <summary>
/// Request to atomically persist multiple field changes on a statement draft entry.
/// </summary>
public sealed record StatementDraftSaveEntryAllRequest(Guid? ContactId, bool? IsCostNeutral, Guid? SavingsPlanId, bool? ArchiveOnBooking, Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);

/// <summary>
/// DTO representing a statement draft with optional split and upload group information.
/// </summary>
public sealed record StatementDraftDto(
    Guid DraftId,
    string OriginalFileName,
    string? Description,
    Guid? DetectedAccountId,
    StatementDraftStatus Status,
    decimal TotalAmount,
    bool IsSplitDraft,
    Guid? ParentDraftId,
    Guid? ParentEntryId,
    decimal? ParentEntryAmount,
    Guid? UploadGroupId,
    IReadOnlyList<StatementDraftEntryDto> Entries);

/// <summary>
/// DTO representing an entry within a statement draft.
/// </summary>
public sealed record StatementDraftEntryDto(
    Guid Id,
    DateTime BookingDate,
    DateTime? ValutaDate,
    decimal Amount,
    string CurrencyCode,
    string Subject,
    string? RecipientName,
    string? BookingDescription,
    bool IsAnnounced,
    bool IsCostNeutral,
    StatementDraftEntryStatus Status,
    Guid? ContactId,
    Guid? SavingsPlanId,
    bool ArchiveSavingsPlanOnBooking,
    Guid? SplitDraftId,
    Guid? SecurityId,
    SecurityTransactionType? SecurityTransactionType,
    decimal? SecurityQuantity,
    decimal? SecurityFeeAmount,
    decimal? SecurityTaxAmount
);
