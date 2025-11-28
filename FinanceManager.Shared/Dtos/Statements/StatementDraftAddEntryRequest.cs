using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to add a new entry to an existing statement draft.
/// </summary>
public sealed record StatementDraftAddEntryRequest([property: Required] DateTime BookingDate, [property: Required] decimal Amount, [property: Required, MaxLength(500)] string Subject);
