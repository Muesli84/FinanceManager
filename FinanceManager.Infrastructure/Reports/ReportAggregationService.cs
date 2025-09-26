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
        // Resolve requested kinds (multi or legacy single)
        var kinds = (query.PostingKinds is { Count: > 0 }
            ? query.PostingKinds.Select(k => (PostingKind)k).Distinct().ToArray()
            : new[] { (PostingKind)query.PostingKind });

        // Determine aggregate period to read (YTD uses monthly raw aggregates)
        var sourcePeriod = query.Interval switch
        {
            ReportInterval.Month => AggregatePeriod.Month,
            ReportInterval.Quarter => AggregatePeriod.Quarter,
            ReportInterval.HalfYear => AggregatePeriod.HalfYear,
            ReportInterval.Year => AggregatePeriod.Year,
            ReportInterval.Ytd => AggregatePeriod.Month,
            _ => AggregatePeriod.Month
        };

        // Normalize analysis date to month start if provided, else use UTC now month start
        var analysis = (query.AnalysisDate?.Date) ?? DateTime.UtcNow.Date;
        analysis = new DateTime(analysis.Year, analysis.Month, 1);

        // Pull raw aggregates for selected kinds & period
        var raw = await _db.PostingAggregates.AsNoTracking()
            .Where(a => a.Period == sourcePeriod && kinds.Contains(a.Kind))
            .Select(a => new
            {
                a.Kind,
                a.PeriodStart,
                a.Amount,
                a.AccountId,
                a.ContactId,
                a.SavingsPlanId,
                a.SecurityId
            }).ToListAsync(ct);

        if (raw.Count == 0)
        {
            return new ReportAggregationResult(query.Interval, Array.Empty<ReportAggregatePointDto>(), query.ComparePrevious, query.CompareYear);
        }

        // Collect entity ids per kind for ownership filtering & name/category lookup
        var accountIds = raw.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).Distinct().ToList();
        var contactIds = raw.Where(r => r.ContactId.HasValue).Select(r => r.ContactId!.Value).Distinct().ToList();
        var savingsIds = raw.Where(r => r.SavingsPlanId.HasValue).Select(r => r.SavingsPlanId!.Value).Distinct().ToList();
        var securityIds = raw.Where(r => r.SecurityId.HasValue).Select(r => r.SecurityId!.Value).Distinct().ToList();

        // Ownership + names
        var accountNames = accountIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Accounts.AsNoTracking()
                .Where(a => accountIds.Contains(a.Id) && a.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var contactNames = contactIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Contacts.AsNoTracking()
                .Where(c => contactIds.Contains(c.Id) && c.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var savingsNames = savingsIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.SavingsPlans.AsNoTracking()
                .Where(s => savingsIds.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var securityNames = securityIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Securities.AsNoTracking()
                .Where(s => securityIds.Contains(s.Id) && s.OwnerUserId == query.OwnerUserId)
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        // Filter raw by ownership (remove aggregates referencing entities not owned)
        raw = raw.Where(r =>
            (r.Kind != PostingKind.Bank || (r.AccountId.HasValue && accountNames.ContainsKey(r.AccountId.Value))) &&
            (r.Kind != PostingKind.Contact || (r.ContactId.HasValue && contactNames.ContainsKey(r.ContactId.Value))) &&
            (r.Kind != PostingKind.SavingsPlan || (r.SavingsPlanId.HasValue && savingsNames.ContainsKey(r.SavingsPlanId.Value))) &&
            (r.Kind != PostingKind.Security || (r.SecurityId.HasValue && securityNames.ContainsKey(r.SecurityId.Value)))
        ).ToList();

        // Category mappings (per entity kind supporting categories)
        var contactCategoryMap = new Dictionary<Guid, Guid?>();
        var savingsCategoryMap = new Dictionary<Guid, Guid?>();
        var securityCategoryMap = new Dictionary<Guid, Guid?>();
        var contactCategoryNames = new Dictionary<Guid, string>();
        var savingsCategoryNames = new Dictionary<Guid, string>();
        var securityCategoryNames = new Dictionary<Guid, string>();

        if (query.IncludeCategory)
        {
            if (contactIds.Count > 0)
            {
                contactCategoryMap = await _db.Contacts.AsNoTracking()
                    .Where(c => contactIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = contactCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    contactCategoryNames = await _db.ContactCategories.AsNoTracking()
                        .Where(c => catIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
            if (savingsIds.Count > 0)
            {
                savingsCategoryMap = await _db.SavingsPlans.AsNoTracking()
                    .Where(s => savingsIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = savingsCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    savingsCategoryNames = await _db.SavingsPlanCategories.AsNoTracking()
                        .Where(c => catIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
            if (securityIds.Count > 0)
            {
                securityCategoryMap = await _db.Securities.AsNoTracking()
                    .Where(s => securityIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.CategoryId })
                    .ToDictionaryAsync(x => x.Id, x => x.CategoryId, ct);
                var catIds = securityCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
                if (catIds.Count > 0)
                {
                    securityCategoryNames = await _db.SecurityCategories.AsNoTracking()
                        .Where(c => catIds.Contains(c.Id))
                        .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                }
            }
        }

        var points = new List<ReportAggregatePointDto>();
        bool multi = kinds.Length > 1;

        // Helper local functions --------------------------------------------------
        static string TypeKey(PostingKind k) => $"Type:{k}";
        static string CategoryKey(PostingKind k, Guid? id) => id.HasValue ? $"Category:{k}:{id}" : $"Category:{k}:_none";
        static bool SupportsCategories(PostingKind k) => k is PostingKind.Contact or PostingKind.SavingsPlan or PostingKind.Security;

        // 1) Aggregate entity (leaf) level per period
        var entityGroups = raw.GroupBy(r => new { r.Kind, r.PeriodStart, r.AccountId, r.ContactId, r.SavingsPlanId, r.SecurityId })
            .Select(g => new
            {
                g.Key.Kind,
                g.Key.PeriodStart,
                Amount = g.Sum(x => x.Amount),
                g.Key.AccountId,
                g.Key.ContactId,
                g.Key.SavingsPlanId,
                g.Key.SecurityId
            })
            .OrderBy(g => g.PeriodStart)
            .ToList();

        // 2) Build category & type aggregates (if needed)
        // Entity rows (with parent assignment if multi or category grouping active)
        foreach (var e in entityGroups)
        {
            string groupKey;
            string groupName;
            Guid? categoryId = null;
            string? categoryName = null;

            if (e.AccountId.HasValue)
            {
                groupKey = $"Account:{e.AccountId}";
                groupName = accountNames.TryGetValue(e.AccountId.Value, out var n) ? n : e.AccountId.Value.ToString("N")[..6];
            }
            else if (e.ContactId.HasValue)
            {
                groupKey = $"Contact:{e.ContactId}";
                groupName = contactNames.TryGetValue(e.ContactId.Value, out var n) ? n : e.ContactId.Value.ToString("N")[..6];
                if (query.IncludeCategory && contactCategoryMap.TryGetValue(e.ContactId.Value, out var cid))
                {
                    categoryId = cid;
                    if (cid.HasValue && contactCategoryNames.TryGetValue(cid.Value, out var cn)) { categoryName = cn; } else if (cid == null) { categoryName = "Uncategorized"; }
                }
            }
            else if (e.SavingsPlanId.HasValue)
            {
                groupKey = $"SavingsPlan:{e.SavingsPlanId}";
                groupName = savingsNames.TryGetValue(e.SavingsPlanId.Value, out var n) ? n : e.SavingsPlanId.Value.ToString("N")[..6];
                if (query.IncludeCategory && savingsCategoryMap.TryGetValue(e.SavingsPlanId.Value, out var cid))
                {
                    categoryId = cid;
                    if (cid.HasValue && savingsCategoryNames.TryGetValue(cid.Value, out var cn)) { categoryName = cn; } else if (cid == null) { categoryName = "Uncategorized"; }
                }
            }
            else if (e.SecurityId.HasValue)
            {
                groupKey = $"Security:{e.SecurityId}";
                groupName = securityNames.TryGetValue(e.SecurityId.Value, out var n) ? n : e.SecurityId.Value.ToString("N")[..6];
                if (query.IncludeCategory && securityCategoryMap.TryGetValue(e.SecurityId.Value, out var cid))
                {
                    categoryId = cid;
                    if (cid.HasValue && securityCategoryNames.TryGetValue(cid.Value, out var cn)) { categoryName = cn; } else if (cid == null) { categoryName = "Uncategorized"; }
                }
            }
            else
            {
                continue;
            }

            string? parent = null;
            if (multi)
            {
                // Parent is category (if supported and requested) else type
                if (query.IncludeCategory && SupportsCategories(e.Kind))
                {
                    parent = CategoryKey(e.Kind, categoryId);
                }
                else
                {
                    parent = TypeKey(e.Kind);
                }
            }
            else if (query.IncludeCategory && SupportsCategories(e.Kind))
            {
                // Single-kind category tree: parent = category key
                parent = CategoryKey(e.Kind, categoryId);
            }
            // In single-kind non-category mode: parent remains null (flat list)

            points.Add(new ReportAggregatePointDto(e.PeriodStart, groupKey, groupName, categoryName, e.Amount, parent, null, null));
        }

        // Category aggregates (per period) (only when categories requested & kind supports)
        if (query.IncludeCategory)
        {
            var categoryAgg = entityGroups
                .Where(e => SupportsCategories(e.Kind))
                .GroupBy(e => new { e.Kind, e.PeriodStart, CategoryId = e.Kind switch
                {
                    PostingKind.Contact => (e.ContactId.HasValue && contactCategoryMap.TryGetValue(e.ContactId.Value, out var cid)) ? cid : null,
                    PostingKind.SavingsPlan => (e.SavingsPlanId.HasValue && savingsCategoryMap.TryGetValue(e.SavingsPlanId.Value, out var sid)) ? sid : null,
                    PostingKind.Security => (e.SecurityId.HasValue && securityCategoryMap.TryGetValue(e.SecurityId.Value, out var secid)) ? secid : null,
                    _ => null
                } })
                .Select(g => new { g.Key.Kind, g.Key.PeriodStart, g.Key.CategoryId, Amount = g.Sum(x => x.Amount) })
                .ToList();

            foreach (var c in categoryAgg)
            {
                string name = c.CategoryId.HasValue
                    ? c.Kind switch
                    {
                        PostingKind.Contact => contactCategoryNames.TryGetValue(c.CategoryId.Value, out var n) ? n : c.CategoryId.Value.ToString("N")[..6],
                        PostingKind.SavingsPlan => savingsCategoryNames.TryGetValue(c.CategoryId.Value, out var n2) ? n2 : c.CategoryId.Value.ToString("N")[..6],
                        PostingKind.Security => securityCategoryNames.TryGetValue(c.CategoryId.Value, out var n3) ? n3 : c.CategoryId.Value.ToString("N")[..6],
                        _ => "Category"
                    }
                    : "Uncategorized";
                var groupKey = CategoryKey(c.Kind, c.CategoryId);
                string? parent = multi ? TypeKey(c.Kind) : null; // in single-kind category tree parent=null (top-level); multi: parent=Type
                points.Add(new ReportAggregatePointDto(c.PeriodStart, groupKey, name, name, c.Amount, parent, null, null));
            }
        }

        // Type aggregates (when multi selection)
        if (multi)
        {
            var groups = points
                .Where(p => !p.GroupKey.StartsWith("Type:"))
                .GroupBy(p => new { p.PeriodStart, Kind = ParseKindFromKey(p.GroupKey) })
                .Where(g => g.Key.Kind.HasValue)
                .ToList();
            foreach (var g in groups)
            {
                var kind = g.Key.Kind!.Value;
                var amount = (query.IncludeCategory && SupportsCategories(kind))
                    ? g.Where(x => x.GroupKey.StartsWith("Category:")).Sum(x => x.Amount)
                    : g.Where(x => !x.GroupKey.StartsWith("Category:")).Sum(x => x.Amount);
                var name = kind switch
                {
                    PostingKind.Bank => "Accounts",
                    PostingKind.Contact => "Contacts",
                    PostingKind.SavingsPlan => "SavingsPlans",
                    PostingKind.Security => "Securities",
                    _ => kind.ToString()
                };
                points.Add(new ReportAggregatePointDto(g.Key.PeriodStart, TypeKey(kind), name, null, amount, null, null, null));
            }
        }

        // YTD transform relative to analysis date --------------------------------
        if (query.Interval == ReportInterval.Ytd && points.Count > 0)
        {
            var cutoffMonth = analysis.Month;
            var currentYear = analysis.Year;
            var ytd = new List<ReportAggregatePointDto>();
            foreach (var grp in points.GroupBy(p => p.GroupKey))
            {
                var byYear = grp.GroupBy(p => p.PeriodStart.Year)
                    .Where(g => g.Key <= currentYear)
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

        // Ensure latest period row based on analysis month ------------------------
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            // Latest period is analysis month for Month/YTD, or nearest period start <= analysis for others
            DateTime latestPeriod;
            if (sourcePeriod == AggregatePeriod.Month)
            {
                latestPeriod = analysis;
            }
            else if (sourcePeriod == AggregatePeriod.Quarter)
            {
                var qIndex = (analysis.Month - 1) / 3; // 0..3
                latestPeriod = new DateTime(analysis.Year, qIndex * 3 + 1, 1);
            }
            else if (sourcePeriod == AggregatePeriod.HalfYear)
            {
                var hIndex = (analysis.Month - 1) / 6; // 0..1
                latestPeriod = new DateTime(analysis.Year, hIndex * 6 + 1, 1);
            }
            else // Year
            {
                latestPeriod = new DateTime(analysis.Year, 1, 1);
            }

            var groups2 = points.GroupBy(p => p.GroupKey).Select(g => new { Key = g.Key, Latest = g.OrderBy(x => x.PeriodStart).Last() }).ToList();
            foreach (var g in groups2)
            {
                if (!points.Any(p => p.GroupKey == g.Key && p.PeriodStart == latestPeriod))
                {
                    points.Add(new ReportAggregatePointDto(latestPeriod, g.Latest.GroupKey, g.Latest.GroupName, g.Latest.CategoryName, 0m, g.Latest.ParentGroupKey, null, null));
                }
            }
        }

        // Previous comparison ----------------------------------------------------
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

        // Year comparison --------------------------------------------------------
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

        // Period Take trimming relative to analysis period -----------------------
        if (query.Take > 0)
        {
            var distinctPeriods = points.Select(p => p.PeriodStart).Distinct().OrderBy(d => d).ToList();
            // Keep the last N periods that are <= latestPeriod based on analysis
            DateTime latestPeriod;
            if (sourcePeriod == AggregatePeriod.Month)
            {
                latestPeriod = analysis;
            }
            else if (sourcePeriod == AggregatePeriod.Quarter)
            {
                var qIndex = (analysis.Month - 1) / 3;
                latestPeriod = new DateTime(analysis.Year, qIndex * 3 + 1, 1);
            }
            else if (sourcePeriod == AggregatePeriod.HalfYear)
            {
                var hIndex = (analysis.Month - 1) / 6;
                latestPeriod = new DateTime(analysis.Year, hIndex * 6 + 1, 1);
            }
            else
            {
                latestPeriod = new DateTime(analysis.Year, 1, 1);
            }
            distinctPeriods = distinctPeriods.Where(d => d <= latestPeriod).ToList();
            if (distinctPeriods.Count > query.Take)
            {
                var keep = distinctPeriods.TakeLast(query.Take).ToHashSet();
                points = points.Where(p => keep.Contains(p.PeriodStart)).ToList();
            }
        }

        // Remove empty latest groups relative to analysis period -----------------
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            DateTime latestPeriod;
            if (sourcePeriod == AggregatePeriod.Month)
            {
                latestPeriod = analysis;
            }
            else if (sourcePeriod == AggregatePeriod.Quarter)
            {
                var qIndex = (analysis.Month - 1) / 3;
                latestPeriod = new DateTime(analysis.Year, qIndex * 3 + 1, 1);
            }
            else if (sourcePeriod == AggregatePeriod.HalfYear)
            {
                var hIndex = (analysis.Month - 1) / 6;
                latestPeriod = new DateTime(analysis.Year, hIndex * 6 + 1, 1);
            }
            else
            {
                latestPeriod = new DateTime(analysis.Year, 1, 1);
            }

            var removable = points.Where(p => p.PeriodStart == latestPeriod)
                .GroupBy(p => p.GroupKey)
                .Where(g =>
                {
                    var r = g.First();
                    var hasPrevData = query.ComparePrevious && r.PreviousAmount.HasValue && r.PreviousAmount.Value != 0m;
                    var hasYearData = query.CompareYear && r.YearAgoAmount.HasValue && r.YearAgoAmount.Value != 0m;
                    return r.Amount == 0m && !hasPrevData && !hasYearData;
                })
                .Select(g => g.Key)
                .ToHashSet();
            if (removable.Count > 0)
            {
                points = points.Where(p => !removable.Contains(p.GroupKey)).ToList();
            }
        }

        // Sort hierarchical: PeriodStart, Type, Category, Entity
        var kindSortOrder = new[] { PostingKind.Bank, PostingKind.Contact, PostingKind.SavingsPlan, PostingKind.Security }
            .Select((k, i) => (k, i))
            .ToDictionary(x => x.k, x => x.i);

        points = points
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.GroupKey.StartsWith("Type:") ? 0 : p.GroupKey.StartsWith("Category:") ? 1 : 2)
            .ThenBy(p =>
            {
                var kind = ParseKindFromKey(p.GroupKey);
                return kind.HasValue && kindSortOrder.TryGetValue(kind.Value, out var idx) ? idx : 999;
            })
            .ThenBy(p => p.GroupName)
            .ToList();

        return new ReportAggregationResult(query.Interval, points, query.ComparePrevious, query.CompareYear);
    }

    private static PostingKind? ParseKindFromKey(string groupKey)
    {
        // Formats:
        // Type:Kind
        // Category:Kind:<id|_none>
        // Account:GUID / Contact:GUID / SavingsPlan:GUID / Security:GUID (derive kind)
        if (groupKey.StartsWith("Type:"))
        {
            var part = groupKey.Split(':')[1];
            if (Enum.TryParse<PostingKind>(part, out var pk)) { return pk; }
            return null;
        }
        if (groupKey.StartsWith("Category:"))
        {
            var part = groupKey.Split(':')[1];
            if (Enum.TryParse<PostingKind>(part, out var pk)) { return pk; }
            return null;
        }
        if (groupKey.StartsWith("Account:")) return PostingKind.Bank;
        if (groupKey.StartsWith("Contact:")) return PostingKind.Contact;
        if (groupKey.StartsWith("SavingsPlan:")) return PostingKind.SavingsPlan;
        if (groupKey.StartsWith("Security:")) return PostingKind.Security;
        return null;
    }
}
