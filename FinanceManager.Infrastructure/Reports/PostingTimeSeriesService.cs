using FinanceManager.Application.Reports;
using FinanceManager.Domain; // PostingKind
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Reports;

public sealed class PostingTimeSeriesService : IPostingTimeSeriesService
{
    private readonly AppDbContext _db;
    public PostingTimeSeriesService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<AggregatePointDto>?> GetAsync(
        Guid ownerUserId,
        PostingKind kind,
        Guid entityId,
        AggregatePeriod period,
        int take,
        CancellationToken ct)
    {
        // Validate ownership depending on kind
        bool owned = kind switch
        {
            PostingKind.Bank => await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == entityId && a.OwnerUserId == ownerUserId, ct),
            PostingKind.Contact => await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == entityId && c.OwnerUserId == ownerUserId, ct),
            PostingKind.SavingsPlan => await _db.SavingsPlans.AsNoTracking().AnyAsync(p => p.Id == entityId && p.OwnerUserId == ownerUserId, ct),
            PostingKind.Security => await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == entityId && s.OwnerUserId == ownerUserId, ct),
            _ => false
        };
        if (!owned) { return null; }

        take = Math.Clamp(take <= 0 ? (period == AggregatePeriod.Month ? 36 : period == AggregatePeriod.Quarter ? 16 : period == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);

        var q = _db.PostingAggregates.AsNoTracking().Where(pa => pa.Kind == kind && pa.Period == period);
        q = kind switch
        {
            PostingKind.Bank => q.Where(a => a.AccountId == entityId),
            PostingKind.Contact => q.Where(a => a.ContactId == entityId),
            PostingKind.SavingsPlan => q.Where(a => a.SavingsPlanId == entityId),
            PostingKind.Security => q.Where(a => a.SecurityId == entityId),
            _ => q.Where(_ => false)
        };

        var latest = await q.OrderByDescending(a => a.PeriodStart).Take(take).ToListAsync(ct);
        return latest.OrderBy(a => a.PeriodStart).Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList();
    }

    public async Task<IReadOnlyList<AggregatePointDto>> GetAllAsync(
        Guid ownerUserId,
        PostingKind kind,
        AggregatePeriod period,
        int take,
        CancellationToken ct)
    {
        take = Math.Clamp(take <= 0 ? (period == AggregatePeriod.Month ? 36 : period == AggregatePeriod.Quarter ? 16 : period == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);

        // Filter aggregates for owned entities of the given kind
        var aggregates = _db.PostingAggregates.AsNoTracking().Where(a => a.Kind == kind && a.Period == period);
        aggregates = kind switch
        {
            PostingKind.Bank => aggregates.Where(a => _db.Accounts.AsNoTracking().Any(ac => ac.Id == a.AccountId && ac.OwnerUserId == ownerUserId)),
            PostingKind.Contact => aggregates.Where(a => _db.Contacts.AsNoTracking().Any(c => c.Id == a.ContactId && c.OwnerUserId == ownerUserId)),
            PostingKind.SavingsPlan => aggregates.Where(a => _db.SavingsPlans.AsNoTracking().Any(s => s.Id == a.SavingsPlanId && s.OwnerUserId == ownerUserId)),
            PostingKind.Security => aggregates.Where(a => _db.Securities.AsNoTracking().Any(s => s.Id == a.SecurityId && s.OwnerUserId == ownerUserId)),
            _ => aggregates.Where(_ => false)
        };

        // Aggregate sums across all entities per period
        var latestDesc = await aggregates
            .GroupBy(a => a.PeriodStart)
            .Select(g => new { PeriodStart = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.PeriodStart)
            .Take(take)
            .ToListAsync(ct);

        return latestDesc
            .OrderBy(x => x.PeriodStart)
            .Select(x => new AggregatePointDto(x.PeriodStart, x.Amount))
            .ToList();
    }
}
