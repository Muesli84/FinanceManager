namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result object returned from validation containing all messages and optional budget impact evaluation.
/// </summary>
/// <param name="DraftId">Validated draft id.</param>
/// <param name="IsValid">True when no errors were found.</param>
/// <param name="Messages">List of validation messages.</param>
/// <returns>The result.</returns>
public sealed record DraftValidationResultDto(Guid DraftId, bool IsValid, IReadOnlyList<DraftValidationMessageDto> Messages)
{
    /// <summary>
    /// Budget impact summary for this draft, populated during validation.
    /// </summary>
    public BookingImpactSummaryDto? BudgetImpact { get; init; }
}
