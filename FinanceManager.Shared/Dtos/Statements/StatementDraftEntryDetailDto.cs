namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Entry detail response payload including neighbors and split info.
/// </summary>
/// <param name="DraftId">The draft id.</param>
/// <param name="OriginalFileName">The original file name.</param>
/// <param name="Entry">The entry.</param>
/// <param name="PrevEntryId">The prev entry id.</param>
/// <param name="NextEntryId">The next entry id.</param>
/// <param name="NextOpenEntryId">The next open entry id.</param>
/// <param name="SplitSum">The split sum.</param>
/// <param name="Difference">The difference.</param>
/// <param name="BankContactId">The bank contact id.</param>
/// <returns>The result.</returns>
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
