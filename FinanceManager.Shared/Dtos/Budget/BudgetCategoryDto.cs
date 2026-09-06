namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Data transfer object for budget categories.
/// </summary>
/// <param name="Id">Identifier of the entity.</param>
/// <param name="OwnerUserId">The owner user id.</param>
/// <param name="Name">The name.</param>
/// <returns>The result.</returns>
public sealed record BudgetCategoryDto(Guid Id, Guid OwnerUserId, string Name);
