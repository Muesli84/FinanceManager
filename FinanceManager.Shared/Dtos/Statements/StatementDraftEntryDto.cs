namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// DTO representing an entry within a statement draft.
/// </summary>
/// <returns>The result.</returns>
public sealed record StatementDraftEntryDto(
    Guid Id,
    int EntryNumber,
    DateTime BookingDate,
    DateTime? ValutaDate,
    decimal Amount,
    string CurrencyCode,
    string Subject,
    string? RecipientName,
    string? BookingDescription,
    bool IsAnnounced,
    bool IsCostNeutral,
    StatementDraftEntryStatus Status,
    Guid? ContactId,
    Guid? SavingsPlanId,
    bool ArchiveSavingsPlanOnBooking,
    Guid? SplitDraftId,
    Guid? SecurityId,
    SecurityTransactionType? SecurityTransactionType,
    decimal? SecurityQuantity,
    decimal? SecurityFeeAmount,
    decimal? SecurityTaxAmount
)
{
    /// <summary>
    /// Optional real-time budget impact evaluation for this entry.
    /// </summary>
    public BudgetImpactEvaluationDto? BudgetImpact { get; init; }
}
