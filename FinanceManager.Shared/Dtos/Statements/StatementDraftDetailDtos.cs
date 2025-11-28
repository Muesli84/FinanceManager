namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Detailed statement draft response including navigation helpers within an upload group.
/// </summary>
public sealed record StatementDraftDetailDto(
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
    IReadOnlyList<StatementDraftEntryDto> Entries,
    Guid? PrevInUpload,
    Guid? NextInUpload
);
