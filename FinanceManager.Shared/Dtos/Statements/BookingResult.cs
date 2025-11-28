namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result of a booking attempt for a draft or single entry.
/// </summary>
/// <param name="Success">True if booking completed successfully.</param>
/// <param name="HasWarnings">True if warnings were present (may require confirmation).</param>
/// <param name="Validation">Validation result information.</param>
/// <param name="StatementImportId">Identifier of created statement import, if any.</param>
/// <param name="TotalEntries">Number of booked entries.</param>
/// <param name="nextDraftId">Id of next draft to process (navigation helper).</param>
public sealed record BookingResult(bool Success, bool HasWarnings, DraftValidationResultDto Validation, Guid? StatementImportId, int? TotalEntries, Guid? nextDraftId);
