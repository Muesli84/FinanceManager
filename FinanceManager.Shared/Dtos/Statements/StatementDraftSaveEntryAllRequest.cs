using System.Text.Json.Serialization;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to atomically persist multiple field changes on a statement draft entry.
/// </summary>
public sealed record StatementDraftSaveEntryAllRequest(
    Guid? ContactId,
    bool? IsCostNeutral,
    Guid? SavingsPlanId,
    bool? ArchiveOnBooking,
    Guid? SecurityId,
    SecurityTransactionType? TransactionType,
    decimal? Quantity,
    decimal? FeeAmount,
    decimal? TaxAmount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SplitDraftId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ClearSplit = null
);
