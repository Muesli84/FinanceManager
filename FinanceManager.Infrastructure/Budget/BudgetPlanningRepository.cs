using FinanceManager.Application.Budget;
using FinanceManager.Domain.Budget;
using FinanceManager.Shared.Dtos.Budget;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Budget;

/// <summary>
/// EF Core implementation of <see cref="IBudgetPlanningRepository"/>.
/// </summary>
public sealed class BudgetPlanningRepository : IBudgetPlanningRepository
{
    private readonly AppDbContext _db;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="db">DbContext instance.</param>
    public BudgetPlanningRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetPurposeIdsAsync(Guid ownerUserId, IReadOnlyCollection<Guid>? purposeIds, CancellationToken ct)
    {
        var purposeQuery = _db.BudgetPurposes.AsNoTracking().Where(p => p.OwnerUserId == ownerUserId);
        if (purposeIds != null && purposeIds.Count > 0)
        {
            purposeQuery = purposeQuery.Where(p => purposeIds.Contains(p.Id));
        }

        return await purposeQuery.Select(p => p.Id).ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<BudgetRule>, IReadOnlyList<BudgetOverride>)> GetRulesAndOverridesAsync(Guid ownerUserId, IReadOnlyList<Guid> purposeIds, BudgetPeriodKey @from, BudgetPeriodKey to, CancellationToken ct)
    {
        @from.Validate();
        to.Validate();

        var fromDate = @from.StartDate;
        var toDate = to.EndDate;

        // Load purposes once to know their category assignment
        var purposes = await _db.BudgetPurposes.AsNoTracking()
            .Where(p => p.OwnerUserId == ownerUserId)
            .Where(p => purposeIds.Contains(p.Id))
            .Select(p => new { p.Id, p.BudgetCategoryId })
            .ToListAsync(ct);

        var purposeCategoryIds = purposes.Where(p => p.BudgetCategoryId != null).Select(p => p.BudgetCategoryId!.Value).Distinct().ToList();

        // Purpose-scoped rules
        var purposeRules = await _db.BudgetRules.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .Where(r => r.BudgetPurposeId != null && purposeIds.Contains(r.BudgetPurposeId.Value))
            .Where(r => r.StartDate <= toDate)
            .Where(r => r.EndDate == null || r.EndDate.Value >= fromDate)
            .ToListAsync(ct);

        // Category-scoped rules (will be expanded)
        var categoryRules = purposeCategoryIds.Count == 0
            ? new List<BudgetRule>()
            : await _db.BudgetRules.AsNoTracking()
                .Where(r => r.OwnerUserId == ownerUserId)
                .Where(r => r.BudgetCategoryId != null && purposeCategoryIds.Contains(r.BudgetCategoryId.Value))
                .Where(r => r.StartDate <= toDate)
                .Where(r => r.EndDate == null || r.EndDate.Value >= fromDate)
                .ToListAsync(ct);

        // Expand each category rule into a synthetic rule per purpose in that category
        var expanded = new List<BudgetRule>(capacity: purposeRules.Count + categoryRules.Count * 2);
        expanded.AddRange(purposeRules);

        foreach (var cr in categoryRules)
        {
            var cId = cr.BudgetCategoryId;
            if (cId == null || cId == Guid.Empty)
            {
                continue;
            }

            foreach (var p in purposes.Where(p => p.BudgetCategoryId == cId))
            {
                var clone = new BudgetRule(cr.OwnerUserId, budgetPurposeId: p.Id, budgetCategoryId: null, cr.Amount, cr.Interval, cr.StartDate, cr.EndDate, cr.CustomIntervalMonths)
                {
                    // keep deterministic behavior; clone gets its own Id but it is not persisted
                };
                expanded.Add(clone);
            }
        }

        var overrides = await _db.BudgetOverrides.AsNoTracking()
            .Where(o => o.OwnerUserId == ownerUserId)
            .Where(o => purposeIds.Contains(o.BudgetPurposeId))
            .Where(o => (o.PeriodYear > @from.Year || (o.PeriodYear == @from.Year && o.PeriodMonth >= @from.Month)))
            .Where(o => (o.PeriodYear < to.Year || (o.PeriodYear == to.Year && o.PeriodMonth <= to.Month)))
            .ToListAsync(ct);

        return (expanded, overrides);
    }
}
