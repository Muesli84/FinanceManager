using FinanceManager.Shared.Dtos.Budget;

namespace FinanceManager.Application.Budget;

/// <summary>
/// Service for managing budget rules.
/// </summary>
public interface IBudgetRuleService
{
    /// <summary>
    /// Creates a new rule for a purpose.
    /// </summary>
    /// <summary>
    /// Backwards-compatible overload: Creates a new rule for a purpose without purpose pattern.
    /// Delegates to the full CreateAsync overload with purposePattern=null and useRegex=false.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto> CreateAsync(Guid ownerUserId, Guid budgetPurposeId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct);

    /// <summary>
    /// Creates a new rule for a purpose.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="purposePattern">The purpose pattern.</param>
    /// <param name="useRegex">The use regex.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto> CreateAsync(Guid ownerUserId, Guid budgetPurposeId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct);

    /// <summary>
    /// Backwards-compatible overload: Creates a new rule for a category without purpose pattern.
    /// Delegates to the full CreateForCategoryAsync overload with purposePattern=null and useRegex=false.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto> CreateForCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct);

    /// <summary>
    /// Creates a new rule for a category.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="purposePattern">The purpose pattern.</param>
    /// <param name="useRegex">The use regex.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto> CreateForCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct);

    /// <summary>
    /// Backwards-compatible overload: Updates an existing rule without purpose pattern.
    /// Delegates to the full UpdateAsync overload with purposePattern=null and useRegex=false.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto?> UpdateAsync(Guid id, Guid ownerUserId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct);

    /// <summary>
    /// Updates an existing rule.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="interval">The interval.</param>
    /// <param name="customIntervalMonths">The custom interval months.</param>
    /// <param name="startDate">The start date.</param>
    /// <param name="endDate">The end date.</param>
    /// <param name="purposePattern">The purpose pattern.</param>
    /// <param name="useRegex">The use regex.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto?> UpdateAsync(Guid id, Guid ownerUserId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct);

    /// <summary>
    /// Deletes an existing rule.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Gets a rule by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BudgetRuleDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Lists rules for a purpose.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BudgetRuleDto>> ListByPurposeAsync(Guid ownerUserId, Guid budgetPurposeId, CancellationToken ct);

    /// <summary>
    /// Lists rules for a category.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BudgetRuleDto>> ListByCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, CancellationToken ct);
}
