namespace FinanceManager.Shared.Dtos.Statements;

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
