using FinanceManager.Domain;
using FinanceManager.Domain.Statements;

namespace FinanceManager.Application.Statements;

public interface IStatementDraftService
{
    Task<StatementDraftDto> CreateDraftAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct);
    Task<StatementDraftDto?> GetDraftAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct);
    Task<CommitResult?> CommitAsync(Guid draftId, Guid ownerUserId, Guid accountId, ImportFormat format, CancellationToken ct);
    Task<bool> CancelAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> ClassifyAsync(Guid draftId, Guid ownerUserId, CancellationToken ct);
    Task<StatementDraftDto?> SetAccountAsync(Guid draftId, Guid ownerUserId, Guid accountId, CancellationToken ct);
    Task<StatementDraftDto?> SetEntryContactAsync(Guid draftId, Guid entryId, Guid? contactId, Guid ownerUserId, CancellationToken ct);
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
    StatementDraftEntryStatus Status,
    Guid? ContactId);

public sealed record StatementDraftDto(Guid DraftId, string OriginalFileName, Guid? DetectedAccountId, StatementDraftStatus Status, IReadOnlyList<StatementDraftEntryDto> Entries);
public sealed record CommitResult(Guid StatementImportId, int TotalEntries);
