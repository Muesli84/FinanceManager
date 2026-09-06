namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to edit the core fields of a statement draft entry.
/// </summary>
/// <param name="BookingDate">The booking date.</param>
/// <param name="ValutaDate">The valuta date.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Subject">The subject.</param>
/// <param name="RecipientName">The recipient name.</param>
/// <param name="CurrencyCode">The currency code.</param>
/// <param name="BookingDescription">The booking description.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftUpdateEntryCoreRequest(DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string? CurrencyCode, string? BookingDescription);
