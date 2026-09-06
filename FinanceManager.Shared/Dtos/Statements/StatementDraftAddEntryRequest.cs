using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to add a new entry to an existing statement draft.
/// </summary>
/// <param name="BookingDate">The booking date.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Subject">The subject.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftAddEntryRequest(
    [param: Required] DateTime BookingDate,
    [param: Required] decimal Amount,
    [param: Required, MaxLength(500)] string Subject
);
