using FinanceManager.Domain;
using FinanceManager.Domain.Statements;
using FinanceManager.Shared.Dtos;
using System.Threading.Tasks;
using FinanceManager.Domain.Securities;

namespace FinanceManager.Application.Statements;

public interface IStatementDraftService
{
    IAsyncEnumerable<StatementDraftDto> CreateDraftAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct);
    Task<StatementDraftDto?> GetDraftAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> GetDraftHeaderAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<IEnumerable<StatementDraftEntryDto>> GetDraftEntriesAsync(Guid draftId, CancellationToken ct);
    Task<StatementDraftEntryDto?> GetDraftEntryAsync(Guid draftId, Guid entryId, CancellationToken ct);
    Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, int skip, int take, CancellationToken ct);
    Task<int> GetOpenDraftsCountAsync(Guid userId, CancellationToken token);
    Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct);
    Task<CommitResult?> CommitAsync(Guid draftId, Guid ownerUserId, Guid accountId, ImportFormat format, CancellationToken ct);
    Task<bool> CancelAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> ClassifyAsync(Guid? draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> SetAccountAsync(Guid draftId, Guid ownerUserId, Guid accountId, CancellationToken ct);
    Task<StatementDraftDto?> SetEntryContactAsync(Guid draftId, Guid entryId, Guid? contactId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> SetEntryCostNeutralAsync(Guid draftId, Guid entryId, bool? isCostNeutral, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto> AssignSavingsPlanAsync(Guid draftId, Guid entryId, Guid? savingsPlanId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> SetEntrySplitDraftAsync(Guid draftId, Guid entryId, Guid? splitDraftId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftEntryDto?> UpdateEntryCoreAsync(Guid draftId, Guid entryId, Guid ownerUserId, DateTime bookingDate, DateTime? valutaDate, decimal amount, string subject, string? recipientName, string? currencyCode, string? bookingDescription, CancellationToken ct);
    Task<StatementDraftDto?> SetEntryArchiveSavingsPlanOnBookingAsync(Guid draftId, Guid entryId, bool archive, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraft?> SetEntrySecurityAsync(Guid draftId,
        Guid entryId,
        Guid? securityId,
        SecurityTransactionType? transactionType,
        decimal? quantity,
        decimal? feeAmount,
        decimal? taxAmount,
        Guid userId,
        CancellationToken ct);
    Task<DraftValidationResultDto> ValidateAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct);
    Task<BookingResult> BookAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool forceWarnings, CancellationToken ct);
    Task<StatementDraftEntryDto?> SaveEntryAllAsync(
        Guid draftId,
        Guid entryId,
        Guid ownerUserId,
        Guid? contactId,
        bool? isCostNeutral,
        Guid? savingsPlanId,
        bool? archiveOnBooking,
        Guid? securityId,
        SecurityTransactionType? transactionType,
        decimal? quantity,
        decimal? feeAmount,
        decimal? taxAmount,
        CancellationToken ct);
}

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

/// <summary>
/// Repräsentiert einen Statement Draft (Import-Entwurf) inkl. optionaler Split-Informationen.
/// Neue Felder (ab Split-Feature):
///  - TotalAmount: Summe aller Eintragsbeträge (immer gefüllt)
///  - IsSplitDraft: True, wenn dieser Draft selbst als Split-Draft (Aufteilungs-Auszug) verknüpft ist (ParentDraftId != null)
///  - ParentDraftId / ParentEntryId / ParentEntryAmount: Referenz auf den Ursprungs-Draft und -Eintrag, falls verknüpft
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
    IReadOnlyList<StatementDraftEntryDto> Entries);

public sealed record CommitResult(Guid StatementImportId, int TotalEntries);

// Validation DTOs
public sealed record DraftValidationMessageDto(
    string Code,
    string Severity, // Error | Warning
    string Message,
    Guid DraftId,
    Guid? EntryId);

public sealed record DraftValidationResultDto(
    Guid DraftId,
    bool IsValid,
    IReadOnlyList<DraftValidationMessageDto> Messages);

public sealed record BookingResult(
    bool Success,
    bool HasWarnings,
    DraftValidationResultDto Validation,
    Guid? StatementImportId,
    int? TotalEntries,
    Guid? nextDraftId);
