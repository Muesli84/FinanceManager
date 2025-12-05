namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Single issue encountered during mass booking (draft-level or entry-level).
/// </summary>
/// <param name="DraftId">Affected draft id.</param>
/// <param name="EntryId">Optional affected entry id (when issue is entry specific).</param>
/// <param name="Code">Stable machine readable code.</param>
/// <param name="Message">Localized/user readable message.</param>
public sealed record StatementDraftMassBookIssueDto(Guid DraftId, Guid? EntryId, string Code, string Message);
