using FinanceManager.Application.Budget;
using FinanceManager.Domain.Budget;
using FinanceManager.Shared.Dtos.Budget;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Budget;

/// <summary>
/// EF Core based implementation of <see cref="IBudgetCategoryService"/>.
/// </summary>
public sealed class BudgetCategoryService : IBudgetCategoryService
{
    private readonly AppDbContext _db;
    private readonly IBudgetPurposeService _purposes;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="db">The db.</param>
    /// <param name="purposes">The purposes.</param>
    public BudgetCategoryService(AppDbContext db, IBudgetPurposeService purposes)
    {
        _db = db;
        _purposes = purposes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetCategoryOverviewDto>> ListOverviewAsync(Guid ownerUserId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var categories = await _db.BudgetCategories.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        if (categories.Count == 0)
        {
            return Array.Empty<BudgetCategoryOverviewDto>();
        }

        // Normalize range
        var effectiveFrom = from;
        var effectiveTo = to;
        if (effectiveFrom.HasValue && effectiveTo.HasValue && effectiveTo.Value < effectiveFrom.Value)
        {
            (effectiveFrom, effectiveTo) = (effectiveTo, effectiveFrom);
        }

        // Precompute purpose counts (independent from range)
        var purposeCountByCategory = await _db.BudgetPurposes.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId && p.BudgetCategoryId != null)
            .GroupBy(p => p.BudgetCategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count, ct);

        Dictionary<Guid, (decimal BudgetSum, decimal ActualSum)> purposeSumsByCategory = new();
        Dictionary<Guid, List<(decimal Amount, BudgetIntervalType Interval, int? CustomIntervalMonths, DateOnly StartDate, DateOnly? EndDate)>> rulesByCategory = new();

        if (effectiveFrom.HasValue && effectiveTo.HasValue)
        {
            var purposeOverviews = await _purposes.ListOverviewAsync(
                ownerUserId,
                0,
                int.MaxValue,
                sourceType: null,
                nameFilter: null,
                effectiveFrom,
                effectiveTo,
                budgetCategoryId: null,
                ct);

            purposeSumsByCategory = purposeOverviews
                .Where(p => p.BudgetCategoryId.HasValue)
                .GroupBy(p => p.BudgetCategoryId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => (BudgetSum: g.Sum(x => x.BudgetSum), ActualSum: g.Sum(x => x.ActualSum)));

            var categoryIds = categories.Select(c => c.Id).ToList();
            var categoryRules = await _db.BudgetRules.AsNoTracking()
                .Where(r => r.OwnerUserId == ownerUserId && r.BudgetCategoryId != null && categoryIds.Contains(r.BudgetCategoryId.Value))
                .Select(r => new { r.BudgetCategoryId, r.Amount, r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate })
                .ToListAsync(ct);

            rulesByCategory = categoryRules
                .GroupBy(r => r.BudgetCategoryId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(r => (r.Amount, r.Interval, r.CustomIntervalMonths, r.StartDate, r.EndDate)).ToList());
        }

        var result = new List<BudgetCategoryOverviewDto>(categories.Count);
        foreach (var c in categories)
        {
            // Category budget: use category scoped rules if present, otherwise sum purpose budgets.
            var budget = 0m;
            var actual = 0m;

            var hasCategoryRules = await _db.BudgetRules.AsNoTracking()
                .AnyAsync(r => r.OwnerUserId == ownerUserId && r.BudgetCategoryId == c.Id, ct);

            if (effectiveFrom.HasValue && effectiveTo.HasValue)
            {
                if (hasCategoryRules)
                {
                    // Compute budget directly from category-scoped rules.
                    if (rulesByCategory.TryGetValue(c.Id, out var rules))
                    {
                        foreach (var rule in rules)
                        {
                            budget += rule.Amount * BudgetPurposeService_CountOccurrencesInRange(rule.Interval, rule.CustomIntervalMonths, rule.StartDate, rule.EndDate, effectiveFrom.Value, effectiveTo.Value);
                        }
                    }

                    if (purposeSumsByCategory.TryGetValue(c.Id, out var sums))
                    {
                        actual = sums.ActualSum;
                    }
                }
                else
                {
                    // Fallback: sum budgets + actuals from purposes in this category.
                    if (purposeSumsByCategory.TryGetValue(c.Id, out var sums))
                    {
                        budget = sums.BudgetSum;
                        actual = sums.ActualSum;
                    }
                }
            }

            purposeCountByCategory.TryGetValue(c.Id, out var purposeCount);
            result.Add(new BudgetCategoryOverviewDto(c.Id, c.Name, budget, actual, actual - budget, purposeCount));
        }

        return result;
    }

    private static int BudgetPurposeService_CountOccurrencesInRange(
        BudgetIntervalType interval,
        int? customIntervalMonths,
        DateOnly start,
        DateOnly? end,
        DateOnly from,
        DateOnly to)
    {
        // Keep consistent with BudgetPurposeService.CountOccurrencesInRange
        var actualEnd = end ?? DateOnly.MaxValue;
        if (start > to || actualEnd < from)
        {
            return 0;
        }

        var effectiveStart = start > from ? start : from;
        var effectiveEnd = actualEnd < to ? actualEnd : to;

        static DateOnly AddMonthsSafe(DateOnly d, int months)
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            var next = dt.AddMonths(months);
            return DateOnly.FromDateTime(next);
        }

        return interval switch
        {
            BudgetIntervalType.Monthly => CountByMonthStep(start, effectiveStart, effectiveEnd, 1),
            BudgetIntervalType.Quarterly => CountByMonthStep(start, effectiveStart, effectiveEnd, 3),
            BudgetIntervalType.Yearly => CountByMonthStep(start, effectiveStart, effectiveEnd, 12),
            BudgetIntervalType.CustomMonths => CountByMonthStep(start, effectiveStart, effectiveEnd, Math.Max(1, customIntervalMonths ?? 1)),
            _ => CountByMonthStep(start, effectiveStart, effectiveEnd, 1)
        };

        static int CountByMonthStep(DateOnly ruleStart, DateOnly rangeStart, DateOnly rangeEnd, int stepMonths)
        {
            var occ = ruleStart;
            if (occ < rangeStart)
            {
                var monthsDiff = (rangeStart.Year - occ.Year) * 12 + (rangeStart.Month - occ.Month);
                var stepsToAdvance = monthsDiff / stepMonths;
                if (stepsToAdvance > 0)
                {
                    occ = AddMonthsSafe(occ, stepsToAdvance * stepMonths);
                }

                while (occ < rangeStart)
                {
                    occ = AddMonthsSafe(occ, stepMonths);
                }
            }

            var count = 0;
            while (occ <= rangeEnd)
            {
                count++;
                occ = AddMonthsSafe(occ, stepMonths);
            }

            return count;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BudgetCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        return await _db.BudgetCategories.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new BudgetCategoryDto(c.Id, c.OwnerUserId, c.Name))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<BudgetCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.BudgetCategories.AsNoTracking()
            .Where(c => c.Id == id && c.OwnerUserId == ownerUserId)
            .Select(c => new BudgetCategoryDto(c.Id, c.OwnerUserId, c.Name))
            .FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public async Task<BudgetCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("OwnerUserId must not be empty", nameof(ownerUserId));
        }

        var entity = new BudgetCategory(ownerUserId, name);
        _db.BudgetCategories.Add(entity);
        await _db.SaveChangesAsync(ct);

        return new BudgetCategoryDto(entity.Id, entity.OwnerUserId, entity.Name);
    }

    /// <inheritdoc />
    public async Task<BudgetCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct)
    {
        var entity = await _db.BudgetCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return null;
        }

        entity.Rename(name);
        await _db.SaveChangesAsync(ct);
        return new BudgetCategoryDto(entity.Id, entity.OwnerUserId, entity.Name);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.BudgetCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return false;
        }

        // Clear category assignment on purposes first.
        var purposes = _db.BudgetPurposes.Where(p => p.OwnerUserId == ownerUserId && p.BudgetCategoryId == id);
        if (_db.Database.IsRelational())
        {
            await purposes.ExecuteUpdateAsync(s => s.SetProperty(p => p.BudgetCategoryId, (Guid?)null), ct);
        }
        else
        {
            foreach (var p in await purposes.ToListAsync(ct))
            {
                p.SetCategory(null);
            }
        }

        _db.BudgetCategories.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
