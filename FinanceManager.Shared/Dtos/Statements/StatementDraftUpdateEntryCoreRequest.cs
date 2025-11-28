namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to edit the core fields of a statement draft entry.
/// </summary>
public sealed record StatementDraftUpdateEntryCoreRequest(DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string? CurrencyCode, string? BookingDescription);
