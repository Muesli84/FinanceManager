using System;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Represents a single expected budget occurrence derived from a budget rule.
/// Contains the originating rule id, optional purpose/category, amount and occurrence date.
/// </summary>
/// <param name="RuleId">The rule id.</param>
/// <param name="PurposeId">The purpose id.</param>
/// <param name="CategoryId">The category id.</param>
/// <param name="Amount">The amount.</param>
/// <param name="Interval">The interval.</param>
/// <param name="OccurrenceDate">The occurrence date.</param>
/// <param name="SourceType">The source type.</param>
/// <param name="SourceId">The source id.</param>
/// <returns>The result.</returns>
public sealed record ExpectedBudgetOccurrenceDto(
    Guid RuleId,
    Guid? PurposeId,
    Guid? CategoryId,
    decimal Amount,
    BudgetIntervalType Interval,
    DateOnly OccurrenceDate,
    BudgetSourceType? SourceType,
    Guid? SourceId);
