namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to control whether a savings plan should be archived once booked.
/// </summary>
public sealed record StatementDraftSetArchiveSavingsPlanOnBookingRequest(bool ArchiveOnBooking);
