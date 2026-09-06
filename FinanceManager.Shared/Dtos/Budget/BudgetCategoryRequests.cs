using System.ComponentModel.DataAnnotations;
using FinanceManager.Shared.Dtos.Common;

namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request for creating a budget category.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Parent">The parent.</param>
/// <returns>The result.</returns>
public sealed record BudgetCategoryCreateRequest(
    [Required, MaxLength(150)] string Name,
    ParentLinkRequest? Parent = null) : CreateRequestWithParent(Parent);

/// <summary>
/// Request for updating a budget category.
/// </summary>
/// <param name="Name">The name.</param>
/// <returns>The result.</returns>
public sealed record BudgetCategoryUpdateRequest(
    [Required, MaxLength(150)] string Name);
