using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to add a new entry to an existing statement draft.
/// </summary>
public sealed record StatementDraftAddEntryRequest(
    [param: Required] DateTime BookingDate,
    [param: Required] decimal Amount,
    [param: Required, MaxLength(500)] string Subject
);
