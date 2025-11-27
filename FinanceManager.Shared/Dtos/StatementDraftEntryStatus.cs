namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Status of a statement draft entry during processing and booking.
/// </summary>
public enum StatementDraftEntryStatus
{
    /// <summary>Entry is open and needs classification.</summary>
    Open = 0,
    /// <summary>Entry is announced and awaiting processing.</summary>
    Announced = 1,
    /// <summary>Entry has an associated contact.</summary>
    Accounted = 2, // Kontakt zugeordnet
    /// <summary>Entry was already booked and detected as duplicate.</summary>
    AlreadyBooked = 3
}