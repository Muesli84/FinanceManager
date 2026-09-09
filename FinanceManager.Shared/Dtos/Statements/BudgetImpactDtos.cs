namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Per-purpose budget impact hint for statement draft entry updates.
/// </summary>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <param name="BudgetPurposeName">The budget purpose name.</param>
/// <param name="BudgetPeriod">The budget period.</param>
/// <param name="HintType">The hint type.</param>
/// <param name="TargetValue">The target value.</param>
/// <param name="ActualBefore">The actual before.</param>
/// <param name="ActualAfter">The actual after.</param>
/// <param name="FulfillmentRateBefore">The fulfillment rate before.</param>
/// <param name="FulfillmentRateAfter">The fulfillment rate after.</param>
/// <param name="Delta">The delta.</param>
/// <param name="Reason">The reason.</param>
/// <returns>The result.</returns>
public sealed record BudgetImpactHintDto(
    Guid? BudgetPurposeId,
    string? BudgetPurposeName,
    string BudgetPeriod,
    BudgetImpactHintType HintType,
    decimal TargetValue,
    decimal ActualBefore,
    decimal ActualAfter,
    decimal FulfillmentRateBefore,
    decimal FulfillmentRateAfter,
    decimal Delta,
    string Reason);

/// <summary>
/// Budget impact result for a single entry interaction.
/// </summary>
/// <param name="EntryId">The entry id.</param>
/// <param name="EvaluatedAtUtc">The evaluated at utc.</param>
/// <param name="EvaluationFingerprint">The evaluation fingerprint.</param>
/// <param name="Hints">The hints.</param>
/// <returns>The result.</returns>
public sealed record BudgetImpactEvaluationDto(
    Guid EntryId,
    DateTime EvaluatedAtUtc,
    string EvaluationFingerprint,
    IReadOnlyList<BudgetImpactHintDto> Hints);

/// <summary>
/// Single summary row for final booking impact output.
/// </summary>
/// <param name="BudgetPurposeId">The budget purpose id.</param>
/// <param name="BudgetPurposeName">The budget purpose name.</param>
/// <param name="BudgetPeriod">The budget period.</param>
/// <param name="HintType">The hint type.</param>
/// <param name="TargetValue">The target value.</param>
/// <param name="ActualBefore">The actual before.</param>
/// <param name="ActualAfter">The actual after.</param>
/// <param name="FulfillmentRateBefore">The fulfillment rate before.</param>
/// <param name="FulfillmentRateAfter">The fulfillment rate after.</param>
/// <param name="Delta">The delta.</param>
/// <param name="Reason">The reason.</param>
/// <returns>The result.</returns>
public sealed record BookingImpactSummaryItemDto(
    Guid? BudgetPurposeId,
    string? BudgetPurposeName,
    string BudgetPeriod,
    BudgetImpactHintType HintType,
    decimal TargetValue,
    decimal ActualBefore,
    decimal ActualAfter,
    decimal FulfillmentRateBefore,
    decimal FulfillmentRateAfter,
    decimal Delta,
    string Reason);

/// <summary>
/// Final summary of budget impact for entry or full draft booking.
/// </summary>
/// <param name="DraftId">The draft id.</param>
/// <param name="EntryId">The entry id.</param>
/// <param name="EvaluatedAtUtc">The evaluated at utc.</param>
/// <param name="EvaluationFingerprint">The evaluation fingerprint.</param>
/// <param name="HighestSeverity">The highest severity.</param>
/// <param name="Items">The items.</param>
/// <returns>The result.</returns>
public sealed record BookingImpactSummaryDto(
    Guid DraftId,
    Guid? EntryId,
    DateTime EvaluatedAtUtc,
    string EvaluationFingerprint,
    BudgetImpactHintType HighestSeverity,
    IReadOnlyList<BookingImpactSummaryItemDto> Items);
