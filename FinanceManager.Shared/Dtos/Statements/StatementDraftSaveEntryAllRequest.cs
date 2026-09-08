using System.Text.Json.Serialization;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request to atomically persist multiple field changes on a statement draft entry.
/// </summary>
/// <param name="ContactId">The contact id.</param>
/// <param name="IsCostNeutral">The is cost neutral.</param>
/// <param name="SavingsPlanId">The savings plan id.</param>
/// <param name="ArchiveOnBooking">The archive on booking.</param>
/// <param name="SecurityId">The security id.</param>
/// <param name="TransactionType">The transaction type.</param>
/// <param name="Quantity">The quantity.</param>
/// <param name="FeeAmount">The fee amount.</param>
/// <param name="TaxAmount">The tax amount.</param>
/// <param name="SplitDraftId">The split draft id.</param>
/// <param name="ClearSplit">The clear split.</param>
/// <returns>The result.</returns>
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
