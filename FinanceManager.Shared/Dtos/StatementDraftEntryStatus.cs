namespace FinanceManager.Shared.Dtos;

public enum StatementDraftEntryStatus
{
    Open = 0,
    Announced = 1,
    Accounted = 2, // Kontakt zugeordnet
    AlreadyBooked = 3
}