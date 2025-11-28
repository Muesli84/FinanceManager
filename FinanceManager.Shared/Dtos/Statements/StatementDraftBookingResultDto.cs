namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result returned from booking a statement draft or a single draft entry.
/// </summary>
/// <param name="Success">True when booking completed successfully (draft or entry committed).</param>
/// <param name="HasWarnings">True when booking was withheld due to warnings and requires user confirmation.</param>
/// <param name="Validation">Validation result containing messages (errors/warnings) relevant to the booking attempt.</param>
/// <param name="StatementImportId">Identifier of the created statement import when the whole draft was committed.</param>
/// <param name="TotalEntries">Total number of entries committed (when whole draft booked).</param>
/// <param name="NextDraftId">If present, id of the next draft in the same upload group that should be opened automatically.</param>
public sealed record StatementDraftBookingResultDto(
    bool Success,
    bool HasWarnings,
    DraftValidationResultDto Validation,
    Guid? StatementImportId,
    int? TotalEntries,
    Guid? NextDraftId);
