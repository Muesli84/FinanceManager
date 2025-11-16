using FinanceManager.Application.Reports;
using FinanceManager.Domain; // PostingKind
using FinanceManager.Domain.Postings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Reports;

public sealed class PostingTimeSeriesService : IPostingTimeSeriesService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PostingTimeSeriesService> _logger;
    public PostingTimeSeriesService(AppDbContext db, ILogger<PostingTimeSeriesService> logger) { _db = db; _logger = logger; }

    // Convenience ctor used in tests that don't provide a logger instance.
    // Uses a NullLogger to avoid requiring test updates; production DI will use the ILogger injected by the container.
    public PostingTimeSeriesService(AppDbContext db) : this(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<PostingTimeSeriesService>.Instance) { }

    private static int ClampTake(AggregatePeriod period, int take)
        => Math.Clamp(take <= 0 ? (period == AggregatePeriod.Month ? 36 : period == AggregatePeriod.Quarter ? 16 : period == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);

    private static DateTime? ComputeMinDate(int? maxYearsBack)
    {
        if (!maxYearsBack.HasValue) { return null; }
        var v = Math.Clamp(maxYearsBack.Value, 1, 10);
        var today = DateTime.UtcNow.Date;
        return new DateTime(today.Year - v, today.Month, 1); // month aligned
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AggregatePointDto>?> GetAsync(
        Guid ownerUserId,
        PostingKind kind,
        Guid entityId,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct)
    {
        _logger.LogInformation("GetAsync called for Owner={OwnerUserId}, Kind={Kind}, EntityId={EntityId}, Period={Period}, Take={Take}", ownerUserId, kind, entityId, period, take);

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

        take = ClampTake(period, take);
        var minDate = ComputeMinDate(maxYearsBack);

        var q = _db.PostingAggregates.AsNoTracking().Where(pa => pa.Kind == kind && pa.Period == period);
        if (minDate.HasValue)
        {
            q = q.Where(a => a.PeriodStart >= minDate.Value);
        }
        q = kind switch
        {
            PostingKind.Bank => q.Where(a => a.AccountId == entityId),
            PostingKind.Contact => q.Where(a => a.ContactId == entityId),
            PostingKind.SavingsPlan => q.Where(a => a.SavingsPlanId == entityId),
            PostingKind.Security => q.Where(a => a.SecurityId == entityId),
            _ => q.Where(_ => false)
        };

        var latest = await q.OrderByDescending(a => a.PeriodStart).Take(take).ToListAsync(ct);
        var result = latest.OrderBy(a => a.PeriodStart).Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList();
        _logger.LogInformation("GetAsync returning {Count} points for EntityId={EntityId}", result.Count, entityId);
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AggregatePointDto>> GetAllAsync(
        Guid ownerUserId,
        PostingKind kind,
        AggregatePeriod period,
        int take,
        int? maxYearsBack,
        CancellationToken ct)
    {
        _logger.LogInformation("GetAllAsync called for Owner={OwnerUserId}, Kind={Kind}, Period={Period}, Take={Take}", ownerUserId, kind, period, take);

        take = ClampTake(period, take);
        var minDate = ComputeMinDate(maxYearsBack);

        // Filter aggregates for owned entities of the given kind
        var aggregates = _db.PostingAggregates.AsNoTracking().Where(a => a.Kind == kind && a.Period == period);
        if (minDate.HasValue)
        {
            aggregates = aggregates.Where(a => a.PeriodStart >= minDate.Value);
        }
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

        var result = latestDesc
            .OrderBy(x => x.PeriodStart)
            .Select(x => new AggregatePointDto(x.PeriodStart, x.Amount))
            .ToList();

        _logger.LogInformation("GetAllAsync returning {Count} aggregated points for Owner={OwnerUserId}", result.Count, ownerUserId);
        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AggregatePointDto>> GetDividendsAsync(Guid ownerUserId, AggregatePeriod period, int take, int? maxYearsBack, CancellationToken ct)
    {
        _logger.LogInformation("GetDividendsAsync called for Owner={OwnerUserId}, Period={Period}, Take={Take}", ownerUserId, period, take);

        // Dividends are defined as security postings with a specific subtype. We group by quarter start.
        take = ClampTake(period, take);

        var today = DateTime.UtcNow.Date;
        var minDate = ComputeMinDate(maxYearsBack) ?? new DateTime(today.Year - 1, 1, 1);

        // Owned securities
        var securityIds = await _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == ownerUserId).Select(s => s.Id).ToListAsync(ct);
        if (securityIds.Count == 0)
        {
            _logger.LogInformation("GetDividendsAsync no owned securities for Owner={OwnerUserId}", ownerUserId);
            return Array.Empty<AggregatePointDto>();
        }

        const int SecurityPostingSubType_Dividend = 2; // keep mapping in sync with client

        var raw = await _db.Postings.AsNoTracking()
            .Where(p => p.Kind == PostingKind.Security)
            .Where(p => p.SecuritySubType.HasValue && (int)p.SecuritySubType.Value == SecurityPostingSubType_Dividend)
            .Where(p => p.SecurityId != null && securityIds.Contains(p.SecurityId.Value))
            .Where(p => p.BookingDate >= minDate)
            .Select(p => new { p.BookingDate, p.Amount })
            .ToListAsync(ct);

        // Group by quarter start
        var groups = raw.GroupBy(x => QuarterStart(x.BookingDate))
            .Select(g => new { PeriodStart = g.Key, Amount = g.Sum(x => x.Amount) })
            .OrderByDescending(g => g.PeriodStart)
            .Take(take)
            .ToList();

        var result = groups.OrderBy(g => g.PeriodStart).Select(g => new AggregatePointDto(g.PeriodStart, g.Amount)).ToList();
        _logger.LogInformation("GetDividendsAsync returning {Count} points for Owner={OwnerUserId}", result.Count, ownerUserId);
        return result;
    }

    private static DateTime QuarterStart(DateTime d)
    {
        int qMonth = ((d.Month - 1) / 3) * 3 + 1; // 1,4,7,10
        return new DateTime(d.Year, qMonth, 1);
    }
}
