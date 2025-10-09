using FinanceManager.Application.Reports;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Reports;
using FinanceManager.Domain; // ensure PostingKind enum
using Microsoft.EntityFrameworkCore;
using System.Linq;

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

        // Special-case: When only Security is requested and IncludeDividendRelated=true,
        // compute net dividends per security directly from Postings grouped by GroupId and period,
        // then merge into the standard aggregate flow (monthly base).
        var includeDividendRelated = query.Filters?.IncludeDividendRelated == true;
        var onlySecurityKind = kinds.Length == 1 && kinds[0] == PostingKind.Security;
        if (includeDividendRelated && onlySecurityKind)
        {
            var result = await QuerySecurityDividendsNetAsync(query, ct);
            return result;
        }

        // Determine aggregate period to read (YTD uses monthly raw aggregates)
        var sourcePeriod = query.Interval switch
        {
            ReportInterval.Month => AggregatePeriod.Month,
            ReportInterval.Quarter => AggregatePeriod.Quarter,
            ReportInterval.HalfYear => AggregatePeriod.HalfYear,
            ReportInterval.Year => AggregatePeriod.Year,
            ReportInterval.Ytd => AggregatePeriod.Month,
            ReportInterval.AllHistory => AggregatePeriod.Month,
            _ => AggregatePeriod.Month
        };
        var ytdMode = query.Interval == ReportInterval.Ytd;
        var allHistoryMode = query.Interval == ReportInterval.AllHistory;

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
                a.SecurityId,
                a.SecuritySubType
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

        // Apply optional top-level filters --------------------------------------------------------
        if (query.Filters is not null)
        {
            static HashSet<Guid>? ToSet(IReadOnlyCollection<Guid>? src) => src is { Count: > 0 } ? src.ToHashSet() : null;
            var f = query.Filters;
            var allowedAccounts = ToSet(f.AccountIds);
            var allowedContacts = ToSet(f.ContactIds);
            var allowedSavings = ToSet(f.SavingsPlanIds);
            var allowedSecurities = ToSet(f.SecurityIds);
            var allowedContactCats = ToSet(f.ContactCategoryIds);
            var allowedSavingsCats = ToSet(f.SavingsPlanCategoryIds);
            var allowedSecurityCats = ToSet(f.SecurityCategoryIds);
            // Only honor subtype filter when Security kind is actually part of the request
            var includesSecurity = kinds.Contains(PostingKind.Security);
            var allowedSecSubTypes = includesSecurity && f.SecuritySubTypes is { Count: > 0 } ? f.SecuritySubTypes.ToHashSet() : null; // new

            bool hasAnyEntityFilter = allowedAccounts != null || allowedContacts != null || allowedSavings != null || allowedSecurities != null;
            bool hasAnyCategoryFilter = allowedContactCats != null || allowedSavingsCats != null || allowedSecurityCats != null;
            bool hasAnySecSubTypeFilter = allowedSecSubTypes != null;

            if (hasAnyEntityFilter || (query.IncludeCategory && hasAnyCategoryFilter) || hasAnySecSubTypeFilter)
            {
                raw = raw.Where(r =>
                {
                    switch (r.Kind)
                    {
                        case PostingKind.Bank:
                            if (allowedAccounts == null) { return true; }
                            return r.AccountId.HasValue && allowedAccounts.Contains(r.AccountId.Value);
                        case PostingKind.Contact:
                            if (query.IncludeCategory && allowedContactCats != null)
                            {
                                if (!r.ContactId.HasValue) { return false; }
                                if (!contactCategoryMap.TryGetValue(r.ContactId.Value, out var cid)) { return false; }
                                return cid.HasValue && allowedContactCats.Contains(cid.Value);
                            }
                            if (allowedContacts == null) { return true; }
                            return r.ContactId.HasValue && allowedContacts.Contains(r.ContactId.Value);
                        case PostingKind.SavingsPlan:
                            if (query.IncludeCategory && allowedSavingsCats != null)
                            {
                                if (!r.SavingsPlanId.HasValue) { return false; }
                                if (!savingsCategoryMap.TryGetValue(r.SavingsPlanId.Value, out var sid)) { return false; }
                                return sid.HasValue && allowedSavingsCats.Contains(sid.Value);
                            }
                            if (allowedSavings == null) { return true; }
                            return r.SavingsPlanId.HasValue && allowedSavings.Contains(r.SavingsPlanId.Value);
                        case PostingKind.Security:
                            // apply category/entity filter first
                            bool entityOk;
                            if (query.IncludeCategory && allowedSecurityCats != null)
                            {
                                if (!r.SecurityId.HasValue) { return false; }
                                if (!securityCategoryMap.TryGetValue(r.SecurityId.Value, out var secid)) { return false; }
                                entityOk = secid.HasValue && allowedSecurityCats.Contains(secid.Value);
                            }
                            else if (allowedSecurities != null)
                            {
                                entityOk = r.SecurityId.HasValue && allowedSecurities.Contains(r.SecurityId.Value);
                            }
                            else
                            {
                                entityOk = true;
                            }

                            if (!entityOk) { return false; }

                            // then optional sub type filter (best effort on aggregate level)
                            if (allowedSecSubTypes != null)
                            {
                                // Only keep when aggregate has a matching subtype; if null treat as non-match
                                if (!r.SecuritySubType.HasValue) { return false; }
                                return allowedSecSubTypes.Contains((int)r.SecuritySubType.Value);
                            }
                            return true;
                        default:
                            return true;
                    }
                }).ToList();
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
                groupName = accountNames.TryGetValue(e.AccountId.Value, out var nAcc) ? nAcc : e.AccountId.Value.ToString("N")[..6];
            }
            else if (e.ContactId.HasValue)
            {
                groupKey = $"Contact:{e.ContactId}";
                groupName = contactNames.TryGetValue(e.ContactId.Value, out var nCon) ? nCon : e.ContactId.Value.ToString("N")[..6];
                if (query.IncludeCategory && contactCategoryMap.TryGetValue(e.ContactId.Value, out var cidC))
                {
                    categoryId = cidC;
                    if (cidC.HasValue && contactCategoryNames.TryGetValue(cidC.Value, out var cn)) { categoryName = cn; } else if (cidC == null) { categoryName = "Uncategorized"; }
                }
            }
            else if (e.SavingsPlanId.HasValue)
            {
                groupKey = $"SavingsPlan:{e.SavingsPlanId}";
                groupName = savingsNames.TryGetValue(e.SavingsPlanId.Value, out var nSav) ? nSav : e.SavingsPlanId.Value.ToString("N")[..6];
                if (query.IncludeCategory && savingsCategoryMap.TryGetValue(e.SavingsPlanId.Value, out var cidS))
                {
                    categoryId = cidS;
                    if (cidS.HasValue && savingsCategoryNames.TryGetValue(cidS.Value, out var cn2)) { categoryName = cn2; } else if (cidS == null) { categoryName = "Uncategorized"; }
                }
            }
            else if (e.SecurityId.HasValue)
            {
                groupKey = $"Security:{e.SecurityId}";
                if (securityNames.TryGetValue(e.SecurityId.Value, out var nSec)) { groupName = nSec; }
                else { groupName = e.SecurityId.Value.ToString("N")[..6]; }
                if (query.IncludeCategory && securityCategoryMap.TryGetValue(e.SecurityId.Value, out var cidSec))
                {
                    categoryId = cidSec;
                    if (cidSec.HasValue && securityCategoryNames.TryGetValue(cidSec.Value, out var cn3)) { categoryName = cn3; } else if (cidSec == null) { categoryName = "Uncategorized"; }
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

        // AllHistory transform: collapse to a single row per group with cumulative sum across all available data
        if (allHistoryMode && points.Count > 0)
        {
            var hist = new List<ReportAggregatePointDto>();
            foreach (var grp in points.GroupBy(p => p.GroupKey))
            {
                var sum = grp.Sum(x => x.Amount);
                var sample = grp.OrderBy(x => x.PeriodStart).First();
                // Use a constant period start (e.g., DateTime.MinValue.Date truncated to year) to anchor display
                var anchor = new DateTime(2000, 1, 1);
                hist.Add(new ReportAggregatePointDto(anchor, sample.GroupKey, sample.GroupName, sample.CategoryName, sum, sample.ParentGroupKey, null, null));
            }
            points = hist.OrderBy(p => p.GroupKey).ToList();
        }

        // Helper to compute the latest anchoring period based on interval --------
        DateTime ComputeLatestPeriod(ReportInterval interval, DateTime analysisDate)
        {
            if (interval == ReportInterval.Month || interval == ReportInterval.Ytd)
            {
                return analysisDate;
            }
            if (interval == ReportInterval.Quarter)
            {
                var qIndex = (analysisDate.Month - 1) / 3; // 0..3
                return new DateTime(analysisDate.Year, qIndex * 3 + 1, 1);
            }
            if (interval == ReportInterval.HalfYear)
            {
                var hIndex = (analysisDate.Month - 1) / 6; // 0..1
                return new DateTime(analysisDate.Year, hIndex * 6 + 1, 1);
            }
            return new DateTime(analysisDate.Year, 1, 1);
        }

        // Ensure latest period row based on analysis month ------------------------
        if (points.Count > 0)
        {
            var latestPeriod = ComputeLatestPeriod(query.Interval, analysis);
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
            DateTime AlignToQuarterStart(DateTime d)
            {
                var qIndex = (d.Month - 1) / 3; // 0..3
                return new DateTime(d.Year, qIndex * 3 + 1, 1);
            }
            DateTime AlignToHalfYearStart(DateTime d)
            {
                var hIndex = (d.Month - 1) / 6; // 0..1
                return new DateTime(d.Year, hIndex * 6 + 1, 1);
            }

            DateTime PrevPeriod(DateTime d)
            {
                return query.Interval switch
                {
                    ReportInterval.Month => d.AddMonths(-1),
                    ReportInterval.Quarter => AlignToQuarterStart(d).AddMonths(-3),
                    ReportInterval.HalfYear => AlignToHalfYearStart(d).AddMonths(-6),
                    ReportInterval.Year => new DateTime(d.Year - 1, 1, 1),
                    ReportInterval.Ytd => new DateTime(d.Year - 1, 1, 1),
                    _ => d.AddMonths(-1)
                };
            }

            var byGroup = points
                .GroupBy(p => p.GroupKey)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.PeriodStart, x => x.Amount));
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var prevDate = PrevPeriod(p.PeriodStart);
                if (byGroup.TryGetValue(p.GroupKey, out var map) && map.TryGetValue(prevDate, out var prevAmount))
                {
                    points[i] = p with { PreviousAmount = prevAmount };
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

        // Trim to take relative to analysis
        if (query.Take > 0 && points.Count > 0)
        {
            var distinct = points.Select(p => p.PeriodStart).Distinct().OrderBy(d => d).ToList();
            var latest = ComputeLatestPeriod(query.Interval, analysis);
            distinct = distinct.Where(d => d <= latest).ToList();
            if (distinct.Count > query.Take)
            {
                var keep = distinct.TakeLast(query.Take).ToHashSet();
                points = points.Where(p => keep.Contains(p.PeriodStart)).ToList();
            }
        }

        // Remove empty latest groups (same rule as default)
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            var latestPeriod = ComputeLatestPeriod(query.Interval, analysis);
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

        // Sort
        points = points
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.GroupKey.StartsWith("Category:") ? 1 : 2)
            .ThenBy(p => p.GroupName)
            .ToList();

        return new ReportAggregationResult(query.Interval, points, query.ComparePrevious, query.CompareYear);
    }

    private async Task<ReportAggregationResult> QuerySecurityDividendsNetAsync(ReportAggregationQuery query, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var analysis = (query.AnalysisDate?.Date) ?? new DateTime(today.Year, today.Month, 1);
        analysis = new DateTime(analysis.Year, analysis.Month, 1);

        int monthsBack = query.Take > 0 ? query.Take - 1 : 0;
        var startMonth = analysis.AddMonths(-monthsBack);
        var endExclusive = analysis.AddMonths(1);

        var ownedSecurities = await _db.Securities.AsNoTracking()
            .Where(s => s.OwnerUserId == query.OwnerUserId)
            .Select(s => new { s.Id, s.Name, s.CategoryId })
            .ToListAsync(ct);
        if (ownedSecurities.Count == 0)
        {
            return new ReportAggregationResult(query.Interval, Array.Empty<ReportAggregatePointDto>(), query.ComparePrevious, query.CompareYear);
        }
        var ownedIds = ownedSecurities.Select(s => s.Id).ToHashSet();
        var securityNames = ownedSecurities.ToDictionary(s => s.Id, s => s.Name);
        var securityCategoryMap = ownedSecurities.ToDictionary(s => s.Id, s => s.CategoryId);
        var categoryIds = securityCategoryMap.Values.Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToList();
        var securityCategoryNames = categoryIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.SecurityCategories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var f = query.Filters;
        HashSet<Guid>? allowedSecurities = f?.SecurityIds is { Count: > 0 } ? f.SecurityIds.ToHashSet() : null;
        HashSet<Guid>? allowedSecurityCats = (query.IncludeCategory && f?.SecurityCategoryIds is { Count: > 0 }) ? f.SecurityCategoryIds!.ToHashSet() : null;

        var postings = _db.Postings.AsNoTracking()
            .Where(p => p.Kind == PostingKind.Security)
            .Where(p => p.SecurityId != null && ownedIds.Contains(p.SecurityId.Value))
            .Where(p => p.BookingDate >= startMonth && p.BookingDate < endExclusive)
            .Where(p => p.SecuritySubType != null);

        const int SecurityPostingSubType_Dividend = 2;
        const int SecurityPostingSubType_Fee = 3;
        const int SecurityPostingSubType_Tax = 4;

        var dividendGroups = postings
            .Where(p => (int)p.SecuritySubType! == SecurityPostingSubType_Dividend)
            .Select(p => p.GroupId)
            .Distinct();

        var allowedSubTypes = new[] { SecurityPostingSubType_Dividend, SecurityPostingSubType_Fee, SecurityPostingSubType_Tax };

        var rows = await postings
            .Where(p => dividendGroups.Contains(p.GroupId))
            .Where(p => allowedSubTypes.Contains((int)p.SecuritySubType!))
            .Select(p => new { p.BookingDate, p.Amount, p.SecurityId })
            .ToListAsync(ct);

        // Aggregate per security and month
        var monthly = rows
            .GroupBy(r => new { Month = new DateTime(r.BookingDate.Year, r.BookingDate.Month, 1, 0, 0, 0, DateTimeKind.Unspecified), r.SecurityId })
            .Select(g => new { g.Key.Month, g.Key.SecurityId, Amount = g.Sum(x => x.Amount) })
            .ToList();

        // Apply entity/category filters
        monthly = monthly.Where(m =>
        {
            if (!m.SecurityId.HasValue) { return false; }
            if (allowedSecurities != null && !allowedSecurities.Contains(m.SecurityId.Value)) { return false; }
            if (query.IncludeCategory && allowedSecurityCats != null)
            {
                if (!securityCategoryMap.TryGetValue(m.SecurityId.Value, out var cid)) { return false; }
                if (!cid.HasValue || !allowedSecurityCats.Contains(cid.Value)) { return false; }
            }
            return true;
        }).ToList();

        // Build entity points and optional category points
        var points = new List<ReportAggregatePointDto>();
        foreach (var e in monthly)
        {
            var key = $"Security:{e.SecurityId}";
            string name = e.SecurityId.HasValue && securityNames.TryGetValue(e.SecurityId.Value, out var n) ? n : (e.SecurityId?.ToString("N")[..6] ?? string.Empty);
            string? categoryName = null; string? parent = null;
            if (query.IncludeCategory && e.SecurityId.HasValue)
            {
                if (securityCategoryMap.TryGetValue(e.SecurityId.Value, out var cid))
                {
                    if (cid.HasValue && securityCategoryNames.TryGetValue(cid.Value, out var cn)) { categoryName = cn; }
                    else { categoryName = "Uncategorized"; }
                    parent = $"Category:Security:{(cid.HasValue ? cid.Value.ToString() : "_none")}";
                }
            }
            points.Add(new ReportAggregatePointDto(e.Month, key, name, categoryName, e.Amount, parent, null, null));
        }

        // Type/category aggregates similar to default when IncludeCategory=true (optional)
        if (query.IncludeCategory)
        {
            var catAgg = monthly
                .GroupBy(m => new { m.Month, CatId = m.SecurityId.HasValue && securityCategoryMap.TryGetValue(m.SecurityId.Value, out var cid) ? cid : null })
                .Select(g => new { g.Key.Month, g.Key.CatId, Amount = g.Sum(x => x.Amount) })
                .ToList();
            foreach (var c in catAgg)
            {
                string name = c.CatId.HasValue && securityCategoryNames.TryGetValue(c.CatId.Value, out var n) ? n : "Uncategorized";
                var key = $"Category:Security:{(c.CatId.HasValue ? c.CatId.Value.ToString() : "_none")}";
                points.Add(new ReportAggregatePointDto(c.Month, key, name, name, c.Amount, null, null, null));
            }
        }

        // Transform for interval (aggregate months to Quarter/HalfYear/Year or YTD/AllHistory)
        List<ReportAggregatePointDto> TransformToInterval(List<ReportAggregatePointDto> src)
        {
            if (query.Interval == ReportInterval.Month)
            {
                return src;
            }
            if (query.Interval == ReportInterval.Ytd)
            {
                var cutoffMonth = analysis.Month;
                var currentYear = analysis.Year;
                var ytd = new List<ReportAggregatePointDto>();
                foreach (var grp in src.GroupBy(p => p.GroupKey))
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
                return ytd;
            }
            if (query.Interval == ReportInterval.AllHistory)
            {
                var hist = new List<ReportAggregatePointDto>();
                foreach (var grp in src.GroupBy(p => p.GroupKey))
                {
                    var sum = grp.Sum(x => x.Amount);
                    var sample = grp.OrderBy(x => x.PeriodStart).First();
                    var anchor = new DateTime(2000, 1, 1);
                    hist.Add(new ReportAggregatePointDto(anchor, sample.GroupKey, sample.GroupName, sample.CategoryName, sum, sample.ParentGroupKey, null, null));
                }
                return hist;
            }
            // Quarter / HalfYear / Year
            DateTime Map(DateTime d)
            {
                return query.Interval switch
                {
                    ReportInterval.Quarter => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
                    ReportInterval.HalfYear => new DateTime(d.Year, ((d.Month - 1) / 6) * 6 + 1, 1),
                    ReportInterval.Year => new DateTime(d.Year, 1, 1),
                    _ => new DateTime(d.Year, d.Month, 1)
                };
            }
            return src
                .GroupBy(p => new { Period = Map(p.PeriodStart), p.GroupKey, p.GroupName, p.CategoryName, p.ParentGroupKey })
                .Select(g => new ReportAggregatePointDto(g.Key.Period, g.Key.GroupKey, g.Key.GroupName, g.Key.CategoryName, g.Sum(x => x.Amount), g.Key.ParentGroupKey, null, null))
                .ToList();
        }

        points = TransformToInterval(points);

        // Ensure latest period exists
        DateTime ComputeLatestPeriod(ReportInterval interval, DateTime analysisDate)
        {
            if (interval == ReportInterval.Month || interval == ReportInterval.Ytd)
            {
                return analysisDate;
            }
            if (interval == ReportInterval.Quarter)
            {
                var qIndex = (analysisDate.Month - 1) / 3; // 0..3
                return new DateTime(analysisDate.Year, qIndex * 3 + 1, 1);
            }
            if (interval == ReportInterval.HalfYear)
            {
                var hIndex = (analysisDate.Month - 1) / 6; // 0..1
                return new DateTime(analysisDate.Year, hIndex * 6 + 1, 1);
            }
            return new DateTime(analysisDate.Year, 1, 1);
        }

        if (points.Count > 0)
        {
            var latestPeriod = ComputeLatestPeriod(query.Interval, analysis);
            var groups2 = points.GroupBy(p => p.GroupKey).Select(g => new { Key = g.Key, Latest = g.OrderBy(x => x.PeriodStart).Last() }).ToList();
            foreach (var g in groups2)
            {
                if (!points.Any(p => p.GroupKey == g.Key && p.PeriodStart == latestPeriod))
                {
                    points.Add(new ReportAggregatePointDto(latestPeriod, g.Latest.GroupKey, g.Latest.GroupName, g.Latest.CategoryName, 0m, g.Latest.ParentGroupKey, null, null));
                }
            }
        }

        // Compute comparisons
        if (query.ComparePrevious)
        {
            DateTime AlignToQuarterStart(DateTime d) => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1);
            DateTime AlignToHalfYearStart(DateTime d) => new DateTime(d.Year, ((d.Month - 1) / 6) * 6 + 1, 1);
            DateTime PrevPeriod(DateTime d) => query.Interval switch
            {
                ReportInterval.Month => d.AddMonths(-1),
                ReportInterval.Quarter => AlignToQuarterStart(d).AddMonths(-3),
                ReportInterval.HalfYear => AlignToHalfYearStart(d).AddMonths(-6),
                ReportInterval.Year => new DateTime(d.Year - 1, 1, 1),
                ReportInterval.Ytd => new DateTime(d.Year - 1, 1, 1),
                _ => d.AddMonths(-1)
            };
            var byGroup = points
                .GroupBy(p => p.GroupKey)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.PeriodStart, x => x.Amount));
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var prevDate = PrevPeriod(p.PeriodStart);
                if (byGroup.TryGetValue(p.GroupKey, out var map) && map.TryGetValue(prevDate, out var prevAmount))
                {
                    points[i] = p with { PreviousAmount = prevAmount };
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

        // Trim to take relative to analysis
        if (query.Take > 0 && points.Count > 0)
        {
            var distinct = points.Select(p => p.PeriodStart).Distinct().OrderBy(d => d).ToList();
            var latest = ComputeLatestPeriod(query.Interval, analysis);
            distinct = distinct.Where(d => d <= latest).ToList();
            if (distinct.Count > query.Take)
            {
                var keep = distinct.TakeLast(query.Take).ToHashSet();
                points = points.Where(p => keep.Contains(p.PeriodStart)).ToList();
            }
        }

        // Remove empty latest groups (same rule as default)
        if ((query.ComparePrevious || query.CompareYear) && points.Count > 0)
        {
            var latestPeriod = ComputeLatestPeriod(query.Interval, analysis);
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

        // Sort
        points = points
            .OrderBy(p => p.PeriodStart)
            .ThenBy(p => p.GroupKey.StartsWith("Category:") ? 1 : 2)
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
