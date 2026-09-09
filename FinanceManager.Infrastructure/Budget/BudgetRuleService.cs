using FinanceManager.Application.Budget;
using FinanceManager.Domain.Budget;
using FinanceManager.Shared.Dtos.Budget;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Budget;

/// <summary>
/// EF Core based implementation of <see cref="IBudgetRuleService"/>.
/// </summary>
public sealed class BudgetRuleService : IBudgetRuleService
{
    private readonly AppDbContext _db;
    private readonly IReportCacheService? _reportCacheService;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="db">App database context.</param>
    /// <param name="reportCacheService">Optional report cache service invalidated when a rule change affects cached reports.</param>
    public BudgetRuleService(AppDbContext db, IReportCacheService? reportCacheService = null)
    {
        _db = db;
        _reportCacheService = reportCacheService;
    }

    /// <inheritdoc />
    public Task<BudgetRuleDto> CreateAsync(Guid ownerUserId, Guid budgetPurposeId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct)
        => CreateAsync(ownerUserId, budgetPurposeId, amount, interval, customIntervalMonths, startDate, endDate, null, false, ct);

    /// <inheritdoc />
    public async Task<BudgetRuleDto> CreateAsync(Guid ownerUserId, Guid budgetPurposeId, decimal amount, FinanceManager.Shared.Dtos.Budget.BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("OwnerUserId must not be empty", nameof(ownerUserId));
        }

        if (budgetPurposeId == Guid.Empty)
        {
            throw new ArgumentException("BudgetPurposeId must not be empty", nameof(budgetPurposeId));
        }

        var purpose = await _db.BudgetPurposes.AsNoTracking()
            .Where(p => p.Id == budgetPurposeId && p.OwnerUserId == ownerUserId)
            .Select(p => new { p.Id, p.BudgetCategoryId })
            .FirstOrDefaultAsync(ct);

        if (purpose == null)
        {
            throw new ArgumentException("Budget purpose not found", nameof(budgetPurposeId));
        }

        // If the purpose is assigned to a category that already has category-scoped rules, reject creating a purpose-scoped rule.
        if (purpose.BudgetCategoryId.HasValue && purpose.BudgetCategoryId.Value != Guid.Empty)
        {
            var hasCategoryRules = await _db.BudgetRules.AsNoTracking()
                .AnyAsync(r => r.OwnerUserId == ownerUserId && r.BudgetCategoryId == purpose.BudgetCategoryId.Value, ct);
            if (hasCategoryRules)
            {
                throw new InvalidOperationException("Cannot create purpose-scoped rule because category-scoped rules already exist for the assigned category.");
            }
        }

        var entity = new BudgetRule(ownerUserId, budgetPurposeId, budgetCategoryId: null, amount, (FinanceManager.Shared.Dtos.Budget.BudgetIntervalType)interval, startDate, endDate, customIntervalMonths, purposePattern, useRegex);
        _db.BudgetRules.Add(entity);
        await _db.SaveChangesAsync(ct);
        await MarkReportCacheForUpdateAsync(ownerUserId, ct);

        return Map(entity);
    }

    /// <inheritdoc />
    public Task<BudgetRuleDto> CreateForCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct)
        => CreateForCategoryAsync(ownerUserId, budgetCategoryId, amount, interval, customIntervalMonths, startDate, endDate, null, false, ct);

    /// <summary>
    /// Creates a new rule for a category. Validates the category and invariants and persists the rule.
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
    public async Task<BudgetRuleDto> CreateForCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("OwnerUserId must not be empty", nameof(ownerUserId));
        }

        if (budgetCategoryId == Guid.Empty)
        {
            throw new ArgumentException("BudgetCategoryId must not be empty", nameof(budgetCategoryId));
        }

        var categoryExists = await _db.BudgetCategories.AsNoTracking().AnyAsync(c => c.Id == budgetCategoryId && c.OwnerUserId == ownerUserId, ct);
        if (!categoryExists)
        {
            throw new ArgumentException("Budget category not found", nameof(budgetCategoryId));
        }

        // Invariant: A category must not get category-scoped rules if any assigned purpose already has purpose-scoped rules.
        var purposeIdsInCategory = await _db.BudgetPurposes.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId && p.BudgetCategoryId == budgetCategoryId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (purposeIdsInCategory.Count > 0)
        {
            var anyPurposeRules = await _db.BudgetRules.AsNoTracking()
                .AnyAsync(r => r.OwnerUserId == ownerUserId && r.BudgetPurposeId != null && purposeIdsInCategory.Contains(r.BudgetPurposeId.Value), ct);

            if (anyPurposeRules)
            {
                throw new InvalidOperationException("Cannot create category-scoped rule because at least one assigned purpose already has purpose-scoped rules.");
            }
        }

        var entity = new BudgetRule(ownerUserId, budgetPurposeId: null, budgetCategoryId, amount, interval, startDate, endDate, customIntervalMonths, purposePattern, useRegex);
        _db.BudgetRules.Add(entity);
        await _db.SaveChangesAsync(ct);
        await MarkReportCacheForUpdateAsync(ownerUserId, ct);
        return Map(entity);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.BudgetRules.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return false;
        }

        _db.BudgetRules.Remove(entity);
        await _db.SaveChangesAsync(ct);
        await MarkReportCacheForUpdateAsync(ownerUserId, ct);
        return true;
    }

    /// <inheritdoc />
    public async Task<BudgetRuleDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.BudgetRules.AsNoTracking()
            .Where(r => r.Id == id && r.OwnerUserId == ownerUserId)
            .Select(r => new BudgetRuleDto(r.Id, r.OwnerUserId, r.BudgetPurposeId, r.BudgetCategoryId, r.Amount, (FinanceManager.Shared.Dtos.Budget.BudgetIntervalType)r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate, r.PurposePattern, r.PurposePatternIsRegex))
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetRuleDto>> ListByPurposeAsync(Guid ownerUserId, Guid budgetPurposeId, CancellationToken ct)
    {
        return await _db.BudgetRules.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.BudgetPurposeId == budgetPurposeId)
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Interval)
            .Select(r => new BudgetRuleDto(r.Id, r.OwnerUserId, r.BudgetPurposeId, r.BudgetCategoryId, r.Amount, (FinanceManager.Shared.Dtos.Budget.BudgetIntervalType)r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate, r.PurposePattern, r.PurposePatternIsRegex))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetRuleDto>> ListByCategoryAsync(Guid ownerUserId, Guid budgetCategoryId, CancellationToken ct)
    {
        return await _db.BudgetRules.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId && r.BudgetCategoryId == budgetCategoryId)
            .OrderBy(r => r.StartDate)
            .ThenBy(r => r.Interval)
            .Select(r => new BudgetRuleDto(r.Id, r.OwnerUserId, r.BudgetPurposeId, r.BudgetCategoryId, r.Amount, (FinanceManager.Shared.Dtos.Budget.BudgetIntervalType)r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate, r.PurposePattern, r.PurposePatternIsRegex))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public Task<BudgetRuleDto?> UpdateAsync(Guid id, Guid ownerUserId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, CancellationToken ct)
        => UpdateAsync(id, ownerUserId, amount, interval, customIntervalMonths, startDate, endDate, null, false, ct);

    /// <summary>
    /// Updates an existing rule identified by id. Applies new schedule, amount and purpose pattern.
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
    public async Task<BudgetRuleDto?> UpdateAsync(Guid id, Guid ownerUserId, decimal amount, BudgetIntervalType interval, int? customIntervalMonths, DateOnly startDate, DateOnly? endDate, string? purposePattern, bool useRegex, CancellationToken ct)
    {
        var entity = await _db.BudgetRules.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return null;
        }

        entity.SetAmount(amount);
        entity.SetSchedule(interval, startDate, endDate, customIntervalMonths);
        entity.SetPurposePattern(purposePattern, useRegex);

        await _db.SaveChangesAsync(ct);
        await MarkReportCacheForUpdateAsync(ownerUserId, ct);
        return Map(entity);
    }

    private async Task MarkReportCacheForUpdateAsync(Guid ownerUserId, CancellationToken ct)
    {
        if (_reportCacheService == null)
        {
            return;
        }

        await _reportCacheService.MarkAllReportCacheEntriesForUpdateAsync(ownerUserId, ct);
    }

    private static BudgetRuleDto Map(BudgetRule r)
        => new(r.Id, r.OwnerUserId, r.BudgetPurposeId, r.BudgetCategoryId, r.Amount, (FinanceManager.Shared.Dtos.Budget.BudgetIntervalType)r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate, r.PurposePattern, r.PurposePatternIsRegex);
}
