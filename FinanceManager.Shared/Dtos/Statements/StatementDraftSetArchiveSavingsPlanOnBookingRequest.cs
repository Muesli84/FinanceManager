namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to control whether a savings plan should be archived once booked.
/// </summary>
/// <param name="ArchiveOnBooking">The archive on booking.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftSetArchiveSavingsPlanOnBookingRequest(bool ArchiveOnBooking);
