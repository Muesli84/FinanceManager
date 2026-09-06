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
/// <returns>The result.</returns>
public sealed record BookingResult(bool Success, bool HasWarnings, DraftValidationResultDto Validation, Guid? StatementImportId, int? TotalEntries, Guid? nextDraftId)
{
    /// <summary>
    /// Optional booking summary containing final budget impact details.
    /// </summary>
    public BookingImpactSummaryDto? BudgetImpactSummary { get; init; }

    /// <summary>
    /// Optional machine-readable booking error code for deterministic client handling.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Optional retry hint for the associated <see cref="ErrorCode"/>.
    /// </summary>
    public bool? Retryable { get; init; }
}
