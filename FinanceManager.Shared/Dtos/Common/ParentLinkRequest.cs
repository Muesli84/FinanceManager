namespace FinanceManager.Shared.Dtos.Common;

/// <summary>
/// Describes an optional parent context for "create and assign" flows.
/// The server may use this information to immediately assign the created entity to the parent.
/// </summary>
/// <param name="ParentKind">String identifier of the parent kind (for example "budget/purposes").</param>
/// <param name="ParentId">Identifier of the parent entity that should receive the assignment.</param>
/// <param name="Field">Optional field hint on the parent (for example "BudgetCategoryId").</param>
/// <returns>The result.</returns>
public sealed record ParentLinkRequest(string ParentKind, Guid ParentId, string? Field = null);
