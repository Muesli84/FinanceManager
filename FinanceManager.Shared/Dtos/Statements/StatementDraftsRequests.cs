using FinanceManager.Shared.Dtos.Securities;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

public sealed record StatementDraftUploadRequest([property: Required] string FileName);
public sealed record StatementDraftUploadResult(StatementDraftDto? FirstDraft, object? SplitInfo);

public sealed record StatementDraftMassBookRequest(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);
public sealed record StatementDraftSetSplitDraftRequest(Guid? SplitDraftId);

public sealed record StatementDraftAddEntryRequest([property: Required] DateTime BookingDate, [property: Required] decimal Amount, [property: Required, MaxLength(500)] string Subject);
public sealed record StatementDraftCommitRequest(Guid AccountId, ImportFormat Format);
public sealed record StatementDraftSetContactRequest(Guid? ContactId);
public sealed record StatementDraftSetCostNeutralRequest(bool? IsCostNeutral);
public sealed record StatementDraftSetSavingsPlanRequest(Guid? SavingsPlanId);
public sealed record StatementDraftSetArchiveSavingsPlanOnBookingRequest(bool ArchiveOnBooking);
public sealed record StatementDraftUpdateEntryCoreRequest(DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string? CurrencyCode, string? BookingDescription);
public sealed record StatementDraftSetEntrySecurityRequest(Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);
public sealed record StatementDraftSaveEntryAllRequest(Guid? ContactId, bool? IsCostNeutral, Guid? SavingsPlanId, bool? ArchiveOnBooking, Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);

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
