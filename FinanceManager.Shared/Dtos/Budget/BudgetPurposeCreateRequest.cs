using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request for creating a budget purpose.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="SourceType">The source type.</param>
/// <param name="SourceId">The source id.</param>
/// <param name="Description">The description.</param>
/// <param name="BudgetCategoryId">The budget category id.</param>
/// <param name="ValuationType">The valuation type.</param>
/// <returns>The result.</returns>
public sealed record BudgetPurposeCreateRequest(
    [Required, MinLength(2), MaxLength(150)] string Name,
    BudgetSourceType SourceType,
    Guid SourceId,
    [MaxLength(500)] string? Description,
    Guid? BudgetCategoryId,
    BudgetValuationType ValuationType = BudgetValuationType.ExactPostings);
