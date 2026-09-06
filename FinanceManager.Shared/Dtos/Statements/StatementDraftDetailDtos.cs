namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Detailed statement draft response including navigation helpers within an upload group.
/// </summary>
/// <returns>The result.</returns>
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
    Guid? NextInUpload,
    IReadOnlyDictionary<Guid, Guid?>? ContactSymbols = null,
    IReadOnlyDictionary<Guid, Guid?>? SavingsPlanSymbols = null,
    IReadOnlyDictionary<Guid, string>? SavingsPlanNames = null,
    IReadOnlyDictionary<Guid, Guid?>? SecuritySymbols = null,
    IReadOnlyDictionary<Guid, string>? SecurityNames = null,
    IReadOnlyDictionary<Guid, string>? ContactNames = null,
    Guid? AccountBankContactId = null,
    Guid? SelfContactId = null,
    bool IsPreliminary = false
);
