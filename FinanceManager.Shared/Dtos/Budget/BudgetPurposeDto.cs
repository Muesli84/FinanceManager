namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Data transfer object for budget purposes.
/// </summary>
/// <param name="Id">Purpose id.</param>
/// <param name="OwnerUserId">Owner user id.</param>
/// <param name="Name">Purpose name.</param>
/// <param name="Description">Optional description.</param>
/// <param name="SourceType">Source type used to resolve actual values.</param>
/// <param name="SourceId">Identifier of the source entity.</param>
/// <param name="BudgetCategoryId">Optional category id assigned to this purpose.</param>
/// <param name="ValuationType">Determines how matching postings are valued for this purpose.</param>
/// <returns>The result.</returns>
public sealed record BudgetPurposeDto(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    BudgetSourceType SourceType,
    Guid SourceId,
    Guid? BudgetCategoryId,
    BudgetValuationType ValuationType = BudgetValuationType.ExactPostings);
