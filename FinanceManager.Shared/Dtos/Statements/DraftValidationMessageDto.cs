namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// A single validation message for a statement draft or one of its entries.
/// </summary>
/// <param name="Code">Stable validation code.</param>
/// <param name="Severity">Severity string (Error | Warning | Information).</param>
/// <param name="Message">Localized/user-readable message.</param>
/// <param name="DraftId">Affected draft id.</param>
/// <param name="EntryId">Optional affected entry id.</param>
/// <param name="RelatedRecordKind">Optional card route kind of a related record (for example <c>savings-plans</c>).</param>
/// <param name="RelatedRecordId">Optional identifier of the related record card.</param>
/// <returns>The result.</returns>
public sealed record DraftValidationMessageDto(
    string Code,
    string Severity,
    string Message,
    Guid DraftId,
    Guid? EntryId,
    string? RelatedRecordKind = null,
    Guid? RelatedRecordId = null);
