using FinanceManager.Application.Reports;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports;
using FinanceManager.Domain; // ensure PostingKind enum
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Reports;

public sealed class ReportAggregationService : IReportAggregationService
{
    private readonly AppDbContext _db;
    public ReportAggregationService(AppDbContext db) => _db = db;

    public async Task<ReportAggregationResult> QueryAsync(ReportAggregationQuery query, CancellationToken ct)
    {
        var kind = (PostingKind)query.PostingKind;
        var aggregates = _db.PostingAggregates.AsNoTracking().Where(a => a.Kind == kind);

        var period = query.Interval switch
        {
            ReportInterval.Month => AggregatePeriod.Month,
            ReportInterval.Quarter => AggregatePeriod.Quarter,
            ReportInterval.HalfYear => AggregatePeriod.HalfYear,
            ReportInterval.Year => AggregatePeriod.Year,
            ReportInterval.Ytd => AggregatePeriod.Month, // Source data is monthly and will be transformed
            _ => AggregatePeriod.Month
        };
        aggregates = aggregates.Where(a => a.Period == period);

        var raw = await aggregates.Select(a => new
        {
            a.PeriodStart,
            a.Amount,
            a.AccountId,
            a.ContactId,
            a.SavingsPlanId,
            a.SecurityId
        }).ToListAsync(ct);

        var accountNames = new Dictionary<Guid, string>();
        var contactNames = new Dictionary<Guid, string>();
        var savingsNames = new Dictionary<Guid, string>();
        var securityNames = new Dictionary<Guid, string>();
        var categoryNames = new Dictionary<Guid, string>();
        Dictionary<Guid, Guid?> entityCategoryMap = new();

        if (kind == PostingKind.Bank && raw.Any(r => r.AccountId.HasValue))
        {
            var ids = raw.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).Distinct().ToList();
            accountNames = await _db.Accounts.Where(a => ids.Contains(a.Id) && a.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        }
        else if (kind == PostingKind.Contact && raw.Any(r => r.ContactId.HasValue))
        {
            var ids = raw.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
            contactNames = await _db.Contacts.Where(c => ids.Contains(c.Id) && c.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            if (query.IncludeCategory)
            {
                entityCategoryMap = await _db.Contacts.Where(c => ids.Contains(c.Id))
                    .Select(c => new { c.Id, c.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = entityCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    categoryNames = await _db.ContactCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
        }
        else if (kind == PostingKind.SavingsPlan && raw.Any(r => r.SavingsPlanId.HasValue))
        {
            var ids = raw.Where(r => r.SavingsPlanId.HasValue).Select(r => r.SavingsPlanId!.Value).Distinct().ToList();
            savingsNames = await _db.SavingsPlans.Where(s => ids.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            if (query.IncludeCategory)
            {
                entityCategoryMap = await _db.SavingsPlans.Where(s => ids.Contains(s.Id))
                    .Select(s => new { s.Id, s.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = entityCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    categoryNames = await _db.SavingsPlanCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
        }
        else if (kind == PostingKind.Security && raw.Any(r => r.SecurityId.HasValue))
        {
            var ids = raw.Where(r => r.SecurityId.HasValue).Select(r => r.SecurityId!.Value).Distinct().ToList();
            securityNames = await _db.Securities.Where(s => ids.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
            if (query.IncludeCategory)
            {
                entityCategoryMap = await _db.Securities.Where(s => ids.Contains(s.Id))
                    .Select(s => new { s.Id, s.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = entityCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    categoryNames = await _db.SecurityCategories.Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
        }

        var points = new List<ReportAggregatePointDto>();
        bool supportsCategories = kind is PostingKind.Contact or PostingKind.SavingsPlan or PostingKind.Security;

        // CATEGORY LEVEL (always when IncludeCategory for supported kinds) -----------------------
        if (query.IncludeCategory && supportsCategories)
        {
            // CATEGORY TOTAL ROWS
            var categoryTotals = raw.GroupBy(r => new
            {
                r.PeriodStart,
                CategoryId = kind switch
                {
                    PostingKind.Contact => (r.ContactId.HasValue && entityCategoryMap.TryGetValue(r.ContactId.Value, out var cid)) ? cid : null,
                    PostingKind.SavingsPlan => (r.SavingsPlanId.HasValue && entityCategoryMap.TryGetValue(r.SavingsPlanId.Value, out var cid2)) ? cid2 : null,
                    PostingKind.Security => (r.SecurityId.HasValue && entityCategoryMap.TryGetValue(r.SecurityId.Value, out var cid3)) ? cid3 : null,
                    _ => null
                }
            })
            .Select(g => new { g.Key.PeriodStart, g.Key.CategoryId, Amount = g.Sum(x => x.Amount) })
            .OrderBy(g => g.PeriodStart)
            .ToList();

            foreach (var g in categoryTotals)
            {
                var catKey = g.CategoryId.HasValue ? $"Category:{kind}:{g.CategoryId}" : $"Category:{kind}:_none";
                var name = g.CategoryId.HasValue && categoryNames.TryGetValue(g.CategoryId.Value, out var cn) ? cn : "Uncategorized";
                points.Add(new ReportAggregatePointDto(g.PeriodStart, catKey, name, name, g.Amount, null, null, null));
            }

            // ENTITY ROWS (children) with ParentGroupKey referencing category key
            var entityGrouped = raw.GroupBy(r => new { r.PeriodStart, r.AccountId, r.ContactId, r.SavingsPlanId, r.SecurityId })
                .Select(g => new
                {
                    g.Key.PeriodStart,
                    Amount = g.Sum(x => x.Amount),
                    g.Key.AccountId,
                    g.Key.ContactId,
                    g.Key.SavingsPlanId,
                    g.Key.SecurityId
                })
                .ToList();

            foreach (var g in entityGrouped)
            {
                string groupKey;
                string groupName;
                string? parentKey = null;
                if (g.ContactId.HasValue)
                {
                    groupKey = $"Contact:{g.ContactId}";
                    groupName = contactNames.TryGetValue(g.ContactId.Value, out var n) ? n : g.ContactId.Value.ToString("N")[..6];
                    if (entityCategoryMap.TryGetValue(g.ContactId.Value, out var catId))
                    {
                        parentKey = catId.HasValue ? $"Category:{kind}:{catId}" : $"Category:{kind}:_none";
                    }
                }
                else if (g.SavingsPlanId.HasValue)
                {
                    groupKey = $"SavingsPlan:{g.SavingsPlanId}";
                    groupName = savingsNames.TryGetValue(g.SavingsPlanId.Value, out var n) ? n : g.SavingsPlanId.Value.ToString("N")[..6];
                    if (entityCategoryMap.TryGetValue(g.SavingsPlanId.Value, out var catId))
                    {
                        parentKey = catId.HasValue ? $"Category:{kind}:{catId}" : $"Category:{kind}:_none";
                    }
                }
                else if (g.SecurityId.HasValue)
                {
                    groupKey = $"Security:{g.SecurityId}";
                    groupName = securityNames.TryGetValue(g.SecurityId.Value, out var n) ? n : g.SecurityId.Value.ToString("N")[..6];
                    if (entityCategoryMap.TryGetValue(g.SecurityId.Value, out var catId))
                    {
                        parentKey = catId.HasValue ? $"Category:{kind}:{catId}" : $"Category:{kind}:_none";
                    }
                }
                else if (g.AccountId.HasValue)
                {
                    // Accounts have no category grouping here (posting kind bank not in supports)
                    groupKey = $"Account:{g.AccountId}";
                    groupName = accountNames.TryGetValue(g.AccountId.Value, out var n) ? n : g.AccountId.Value.ToString("N")[..6];
                }
                else
                {
                    continue;
                }
                points.Add(new ReportAggregatePointDto(g.PeriodStart, groupKey, groupName, null, g.Amount, parentKey, null, null));
            }
        }
        else
        {
            // ENTITY LEVEL -----------------------
            var grouped = raw
                .GroupBy(r => new { r.PeriodStart, r.AccountId, r.ContactId, r.SavingsPlanId, r.SecurityId })
                .Select(g => new
                {
                    g.Key.PeriodStart,
                    Amount = g.Sum(x => x.Amount),
                    g.Key.AccountId,
                    g.Key.ContactId,
                    g.Key.SavingsPlanId,
                    g.Key.SecurityId
                })
                .OrderBy(g => g.PeriodStart)
                .ToList();

            foreach (var g in grouped)
            {
                string groupKey;
                string groupName;
                if (g.AccountId.HasValue)
                {
                    groupKey = $"Account:{g.AccountId}";
                    groupName = accountNames.TryGetValue(g.AccountId.Value, out var n) ? n : g.AccountId.Value.ToString("N")[..6];
                }
                else if (g.ContactId.HasValue)
                {
                    groupKey = $"Contact:{g.ContactId}";
                    groupName = contactNames.TryGetValue(g.ContactId.Value, out var n) ? n : g.ContactId.Value.ToString("N")[..6];
                }
                else if (g.SavingsPlanId.HasValue)
                {
                    groupKey = $"SavingsPlan:{g.SavingsPlanId}";
                    groupName = savingsNames.TryGetValue(g.SavingsPlanId.Value, out var n) ? n : g.SavingsPlanId.Value.ToString("N")[..6];
                }
                else if (g.SecurityId.HasValue)
                {
                    groupKey = $"Security:{g.SecurityId}";
                    groupName = securityNames.TryGetValue(g.SecurityId.Value, out var n) ? n : g.SecurityId.Value.ToString("N")[..6];
                }
                else
                {
                    continue;
                }
                points.Add(new ReportAggregatePointDto(g.PeriodStart, groupKey, groupName, null, g.Amount, null, null, null));
            }
        }

        // YTD transform (strict: Jan 1 .. current month for each year, using current calendar month as cutoff)
        if (query.Interval == ReportInterval.Ytd && points.Count > 0)
        {
            var now = DateTime.UtcNow;
            var cutoffMonth = now.Month; // always current month
            var currentYear = now.Year;
            var ytd = new List<ReportAggregatePointDto>();
            foreach (var grp in points.GroupBy(p => p.GroupKey))
            {
                var byYear = grp.GroupBy(p => p.PeriodStart.Year)
                    .Where(g => g.Key <= currentYear) // ignore future years if any
                    .Select(g => new
                    {
                        Year = g.Key,
                        Amount = g.Where(x => x.PeriodStart.Month <= cutoffMonth).Sum(x => x.Amount),
                        Sample = g.OrderBy(x => x.PeriodStart).First()
                    })
                    .OrderBy(x => x.Year);
                foreach (var y in byYear)
                {
                    var start = new DateTime(y.Year, 1, 1);
                    ytd.Add(new ReportAggregatePointDto(start, y.Sample.GroupKey, y.Sample.GroupName, y.Sample.CategoryName, y.Amount, y.Sample.ParentGroupKey, null, null));
                }
            }
            points = ytd.OrderBy(p => p.PeriodStart).ThenBy(p => p.GroupKey).ToList();
        }

        // Ensure a row exists for the latest period for every group that has any historical data (creates 0-rows for missing current period)
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            var latestPeriod = points.Max(p => p.PeriodStart);
            var groups = points.GroupBy(p => p.GroupKey).Select(g => new { Key = g.Key, Latest = g.OrderBy(x => x.PeriodStart).Last() }).ToList();
            foreach (var g in groups)
            {
                if (!points.Any(p => p.GroupKey == g.Key && p.PeriodStart == latestPeriod))
                {
                    points.Add(new ReportAggregatePointDto(latestPeriod, g.Latest.GroupKey, g.Latest.GroupName, g.Latest.CategoryName, 0m, g.Latest.ParentGroupKey, null, null));
                }
            }
        }

        if (query.ComparePrevious)
        {
            foreach (var grp in points.GroupBy(p => p.GroupKey))
            {
                ReportAggregatePointDto? prev = null;
                foreach (var p in grp.OrderBy(x => x.PeriodStart))
                {
                    if (prev != null)
                    {
                        var idx = points.FindIndex(x => x.GroupKey == p.GroupKey && x.PeriodStart == p.PeriodStart);
                        points[idx] = points[idx] with { PreviousAmount = prev.Amount };
                    }
                    prev = p;
                }
            }
        }
        if (query.CompareYear)
        {
            var index = points.ToDictionary(p => (p.GroupKey, p.PeriodStart), p => p);
            foreach (var p in points.ToList())
            {
                var yearAgoDate = p.PeriodStart.AddYears(-1);
                if (index.TryGetValue((p.GroupKey, yearAgoDate), out var yearAgo))
                {
                    var idx = points.FindIndex(x => x.GroupKey == p.GroupKey && x.PeriodStart == p.PeriodStart);
                    points[idx] = points[idx] with { YearAgoAmount = yearAgo.Amount };
                }
            }
        }

        if (query.Take > 0)
        {
            var distinctPeriods = points.Select(p => p.PeriodStart).Distinct().OrderBy(d => d).ToList();
            if (distinctPeriods.Count > query.Take)
            {
                var keep = distinctPeriods.TakeLast(query.Take).ToHashSet();
                points = points.Where(p => keep.Contains(p.PeriodStart)).ToList();
            }
        }

        // Remove groups that have no data in current period AND no data in selected comparison baselines
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            var latestPeriod = points.Max(p => p.PeriodStart);
            var removable = points.Where(p => p.PeriodStart == latestPeriod)
                .GroupBy(p => p.GroupKey)
                .Where(g =>
                {
                    var r = g.First();
                    var hasPrevData = query.ComparePrevious && r.PreviousAmount.HasValue && r.PreviousAmount.Value != 0m;
                    var hasYearData = query.CompareYear && r.YearAgoAmount.HasValue && r.YearAgoAmount.Value != 0m;
                    return r.Amount == 0m && !hasPrevData && !hasYearData; // nothing relevant to show
                })
                .Select(g => g.Key)
                .ToHashSet();
            if (removable.Count > 0)
            {
                points = points.Where(p => !removable.Contains(p.GroupKey)).ToList();
            }
        }

        return new ReportAggregationResult(query.Interval, points, query.ComparePrevious, query.CompareYear);
    }
}
