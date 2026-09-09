namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Data transfer object for the budget purposes overview including rule count and computed budget for a period.
/// </summary>
/// <param name="Id">Purpose id.</param>
/// <param name="OwnerUserId">Owner user id.</param>
/// <param name="Name">Purpose name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="SourceType">Source type used to resolve actual values.</param>
/// <param name="SourceId">Identifier of the source entity.</param>
/// <param name="RuleCount">Number of configured rules for this purpose.</param>
/// <param name="BudgetSum">Computed budget sum for the provided period.</param>
/// <param name="ActualSum">Computed actual sum for the provided period.</param>
/// <param name="Variance">Difference between actual and budget (ActualSum - BudgetSum).</param>
/// <param name="SourceName">Resolved display name of the source entity.</param>
/// <param name="SourceSymbolAttachmentId">Optional symbol attachment id of the source entity.</param>
/// <param name="BudgetCategoryId">Optional category id assigned to this purpose.</param>
/// <param name="BudgetCategoryName">Resolved display name of the assigned category.</param>
/// <param name="ValuationType">Determines how matching postings are valued for this purpose.</param>
/// <returns>The result.</returns>
public sealed record BudgetPurposeOverviewDto(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    BudgetSourceType SourceType,
    Guid SourceId,
    int RuleCount,
    decimal BudgetSum,
    decimal ActualSum,
    decimal Variance,
    string? SourceName,
    Guid? SourceSymbolAttachmentId,
    Guid? BudgetCategoryId,
    string? BudgetCategoryName,
    BudgetValuationType ValuationType = BudgetValuationType.ExactPostings);
