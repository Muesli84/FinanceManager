namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Entry detail response payload including neighbors and split info.
/// </summary>
public sealed record StatementDraftEntryDetailDto(
    Guid DraftId,
    string OriginalFileName,
    StatementDraftEntryDto Entry,
    Guid? PrevEntryId,
    Guid? NextEntryId,
    Guid? NextOpenEntryId,
    decimal? SplitSum,
    decimal? Difference,
    Guid? BankContactId
);
