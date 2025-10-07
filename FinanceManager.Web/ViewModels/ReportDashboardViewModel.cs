using System.Net.Http.Json;
using FinanceManager.Domain.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class ReportDashboardViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public ReportDashboardViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    // UI state moved from component
    public bool EditMode { get; set; }

    // Dashboard state
    public bool IsBusy { get; private set; }
    public List<int> SelectedKinds { get; set; } = new() { 0 }; // 0 == PostingKind.Bank
    public int Interval { get; set; } = 0; // ReportInterval.Month
    public bool IncludeCategory { get; set; }
    public bool ComparePrevious { get; set; }
    public bool CompareYear { get; set; }
    public bool ShowChart { get; set; } = true;
    public int Take { get; set; } = 24;

    public Guid? ActiveFavoriteId { get; set; }
    public string FavoriteName { get; set; } = string.Empty;
    public bool ShowFavoriteDialog { get; private set; }
    public bool FavoriteDialogIsUpdate { get; private set; }
    public string? FavoriteError { get; private set; }

    // Filters state (entity level)
    public HashSet<Guid> SelectedAccounts { get; private set; } = new();
    public HashSet<Guid> SelectedContacts { get; private set; } = new();
    public HashSet<Guid> SelectedSavingsPlans { get; private set; } = new();
    public HashSet<Guid> SelectedSecurities { get; private set; } = new();
    // Filters state (category level)
    public HashSet<Guid> SelectedContactCategories { get; private set; } = new();
    public HashSet<Guid> SelectedSavingsCategories { get; private set; } = new();
    public HashSet<Guid> SelectedSecurityCategories { get; private set; } = new();

    public List<PointDto> Points { get; private set; } = new();

    // Expansion state for table rows
    public Dictionary<string, bool> Expanded { get; } = new();
    public void ToggleExpanded(string key)
    {
        if (Expanded.ContainsKey(key))
        {
            Expanded[key] = !Expanded[key];
        }
        else
        {
            Expanded[key] = true;
        }
        RaiseStateChanged();
    }
    public bool IsExpanded(string key) => Expanded.TryGetValue(key, out var v) && v;

    public int PrimaryKind => SelectedKinds.FirstOrDefault();
    public bool IsMulti => SelectedKinds.Count > 1;
    public static bool IsCategorySupported(int kind) => kind == 1 || kind == 2 || kind == 3; // Contact, SavingsPlan, Security
    public bool IsCategoryGroupingSingle => !IsMulti && IncludeCategory && IsCategorySupported(PrimaryKind);

    public IReadOnlyList<PointDto> LatestPerGroup => Points
        .Where(p => !p.GroupKey.StartsWith("Type:") && !p.GroupKey.StartsWith("Category:") && p.ParentGroupKey == null)
        .GroupBy(p => p.GroupKey)
        .Select(g => g.OrderBy(x => x.PeriodStart).Last())
        .OrderBy(p => IsNegative(p))
        .ThenByDescending(p => p.Amount)
        .ToList();

    public FiltersPayload? BuildFiltersPayload()
    {
        if (!IncludeCategory && SelectedAccounts.Count == 0 && SelectedContacts.Count == 0 && SelectedSavingsPlans.Count == 0 && SelectedSecurities.Count == 0)
        {
            return null;
        }
        if (IncludeCategory && SelectedContactCategories.Count == 0 && SelectedSavingsCategories.Count == 0 && SelectedSecurityCategories.Count == 0 && SelectedAccounts.Count == 0)
        {
            return null;
        }
        if (IncludeCategory)
        {
            return new FiltersPayload(
                SelectedAccounts.ToList(),
                null,
                null,
                null,
                SelectedContactCategories.ToList(),
                SelectedSavingsCategories.ToList(),
                SelectedSecurityCategories.ToList());
        }
        else
        {
            return new FiltersPayload(
                SelectedAccounts.ToList(),
                SelectedContacts.ToList(),
                SelectedSavingsPlans.ToList(),
                SelectedSecurities.ToList(),
                null,
                null,
                null);
        }
    }

    public void ClearFilters()
    {
        SelectedAccounts.Clear();
        SelectedContacts.Clear();
        SelectedSavingsPlans.Clear();
        SelectedSecurities.Clear();
        SelectedContactCategories.Clear();
        SelectedSavingsCategories.Clear();
        SelectedSecurityCategories.Clear();
    }

    public async Task ReloadAsync(DateTime? analysisDate, CancellationToken ct = default)
    {
        if (IsBusy) { return; }
        IsBusy = true; RaiseStateChanged();
        try
        {
            var result = await LoadAsync(PrimaryKind, Interval, Take, IncludeCategory, ComparePrevious, CompareYear, IsMulti ? SelectedKinds : null, analysisDate, BuildFiltersPayload(), ct);
            Points = result.Points
                .OrderBy(p => p.GroupKey)
                .ThenBy(p => p.PeriodStart)
                .ToList();
        }
        finally
        {
            IsBusy = false; RaiseStateChanged();
        }
    }

    public IEnumerable<PointDto> GetTopLevelRows()
    {
        if (IsMulti)
        {
            return Points.Where(p => p.GroupKey.StartsWith("Type:"))
                .GroupBy(p => p.GroupKey)
                .Select(g => g.OrderBy(x => x.PeriodStart).Last())
                .OrderBy(p => IsNegative(p))
                .ThenByDescending(p => p.Amount)
                .ToList();
        }
        if (IsCategoryGroupingSingle)
        {
            return Points.Where(p => p.ParentGroupKey == null && p.GroupKey.StartsWith("Category:"))
                .GroupBy(p => p.GroupKey)
                .Select(g => g.OrderBy(x => x.PeriodStart).Last())
                .OrderBy(p => IsNegative(p))
                .ThenByDescending(p => p.Amount);
        }
        return LatestPerGroup
            .OrderBy(p => IsNegative(p))
            .ThenByDescending(p => p.Amount);
    }

    public IEnumerable<PointDto> GetChildRows(string parentKey) => GetChildRowsImpl(parentKey);

    private IEnumerable<PointDto> GetChildRowsImpl(string parentKey)
    {
        if (parentKey.StartsWith("Type:"))
        {
            var kindName = parentKey.Substring("Type:".Length);
            // decide per type whether category children are supported
            int typeKind = kindName switch
            {
                "Bank" => 0,
                "Contact" => 1,
                "SavingsPlan" => 2,
                "Security" => 3,
                _ => PrimaryKind
            };
            var useCategoryChildren = IncludeCategory && IsCategorySupported(typeKind);
            if (useCategoryChildren)
            {
                return Points.Where(p => p.ParentGroupKey == parentKey && p.GroupKey.StartsWith("Category:"))
                    .GroupBy(p => p.GroupKey)
                    .Select(g => g.OrderBy(x => x.PeriodStart).Last())
                    .OrderBy(p => IsNegative(p))
                    .ThenByDescending(p => p.Amount)
                    .ToList();
            }
            var candidates = Points.Where(p => p.ParentGroupKey == parentKey && !p.GroupKey.StartsWith("Category:"));
            return candidates
                .GroupBy(p => p.GroupKey)
                .Select(g => g.OrderBy(x => x.PeriodStart).Last())
                .OrderBy(p => IsNegative(p))
                .ThenByDescending(p => p.Amount)
                .ToList();
        }
        if (parentKey.StartsWith("Category:"))
        {
            return Points.Where(p => p.ParentGroupKey == parentKey)
                .GroupBy(p => p.GroupKey)
                .Select(g => g.OrderBy(x => x.PeriodStart).Last())
                .OrderBy(p => IsNegative(p))
                .ThenByDescending(p => p.Amount)
                .ToList();
        }
        return Array.Empty<PointDto>();
    }

    public bool HasChildren(string key) => GetChildRowsImpl(key).Any();

    // Derived UI helpers
    public bool ShowPreviousColumns => ComparePrevious && ((ReportInterval)Interval is not ReportInterval.Year and not ReportInterval.Ytd);
    public bool ShowCategoryColumn => IncludeCategory && !IsCategoryGroupingSingle;

    public (decimal Amount, decimal? Prev, decimal? Year) GetTotals()
    {
        var rows = GetTopLevelRows().ToList();
        var amount = rows.Sum(r => r.Amount);
        decimal? prev = ShowPreviousColumns ? rows.Where(r => r.PreviousAmount.HasValue).Sum(r => r.PreviousAmount!.Value) : null;
        decimal? year = CompareYear ? rows.Where(r => r.YearAgoAmount.HasValue).Sum(r => r.YearAgoAmount!.Value) : null;
        return (amount, prev, year);
    }

    public IReadOnlyList<(DateTime PeriodStart, decimal Sum)> GetChartByPeriod()
    {
        var byPeriod = Points
            .Where(p => p.ParentGroupKey == null || p.GroupKey.StartsWith("Type:"))
            .GroupBy(p => p.PeriodStart)
            .Select(g => (PeriodStart: g.Key, Sum: g.Where(x => x.ParentGroupKey == null || x.GroupKey.StartsWith("Type:")).Sum(x => x.Amount)))
            .OrderBy(x => x.PeriodStart)
            .ToList();
        return byPeriod;
    }

    public static bool IsNegative(PointDto p)
    {
        if (p.Amount < 0m)
        {
            return true;
        }
        if (p.Amount == 0m)
        {
            var hasPrev = p.PreviousAmount.HasValue;
            var hasYear = p.YearAgoAmount.HasValue;
            if (hasPrev || hasYear)
            {
                var prevNeg = hasPrev && p.PreviousAmount!.Value < 0m;
                var yearNeg = hasYear && p.YearAgoAmount!.Value < 0m;
                if ((!hasPrev || prevNeg) && (!hasYear || yearNeg))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public async Task<AggregationResponse> LoadAsync(int primaryKind, int interval, int take, bool includeCategory, bool comparePrevious, bool compareYear, IReadOnlyCollection<int>? postingKinds, DateTime? analysisDate, FiltersPayload? filters, CancellationToken ct = default)
    {
        var req = new QueryRequest(primaryKind, interval, take, includeCategory, comparePrevious, compareYear, postingKinds, analysisDate, filters);
        var resp = await _http.PostAsJsonAsync("/api/report-aggregates", req, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<AggregationResponse>(cancellationToken: ct) ?? new AggregationResponse(interval, new(), false, false);
        return result;
    }

    public async Task<FavoriteDto?> SaveFavoriteAsync(string name, int primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool showChart, bool expandable, IReadOnlyCollection<int>? postingKinds, FiltersPayload? filters, CancellationToken ct = default)
    {
        var payload = new FavCreate
        {
            Name = name,
            PostingKind = primaryKind,
            IncludeCategory = includeCategory,
            Interval = interval,
            Take = take,
            ComparePrevious = comparePrevious,
            CompareYear = compareYear,
            ShowChart = showChart,
            Expandable = expandable,
            PostingKinds = postingKinds,
            Filters = filters is null ? null : new FavFiltersDto
            {
                AccountIds = filters.AccountIds,
                ContactIds = filters.ContactIds,
                SavingsPlanIds = filters.SavingsPlanIds,
                SecurityIds = filters.SecurityIds,
                ContactCategoryIds = filters.ContactCategoryIds,
                SavingsPlanCategoryIds = filters.SavingsPlanCategoryIds,
                SecurityCategoryIds = filters.SecurityCategoryIds
            }
        };
        var resp = await _http.PostAsJsonAsync("/api/report-favorites", payload, ct);
        if (!resp.IsSuccessStatusCode) { return null; }
        return await resp.Content.ReadFromJsonAsync<FavoriteDto>(cancellationToken: ct);
    }

    public async Task<FavoriteDto?> UpdateFavoriteAsync(Guid id, string name, int primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool showChart, bool expandable, IReadOnlyCollection<int>? postingKinds, FiltersPayload? filters, CancellationToken ct = default)
    {
        var payload = new FavCreate
        {
            Name = name,
            PostingKind = primaryKind,
            IncludeCategory = includeCategory,
            Interval = interval,
            Take = take,
            ComparePrevious = comparePrevious,
            CompareYear = compareYear,
            ShowChart = showChart,
            Expandable = expandable,
            PostingKinds = postingKinds,
            Filters = filters is null ? null : new FavFiltersDto
            {
                AccountIds = filters.AccountIds,
                ContactIds = filters.ContactIds,
                SavingsPlanIds = filters.SavingsPlanIds,
                SecurityIds = filters.SecurityIds,
                ContactCategoryIds = filters.ContactCategoryIds,
                SavingsPlanCategoryIds = filters.SavingsPlanCategoryIds,
                SecurityCategoryIds = filters.SecurityCategoryIds
            }
        };
        var resp = await _http.PutAsJsonAsync($"/api/report-favorites/{id}", payload, ct);
        if (!resp.IsSuccessStatusCode) { return null; }
        return await resp.Content.ReadFromJsonAsync<FavoriteDto>(cancellationToken: ct);
    }

    public async Task<bool> DeleteFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/api/report-favorites/{id}", ct);
        return resp.IsSuccessStatusCode;
    }

    public void OpenFavoriteDialog(bool update, string? resetNameIfNew = null)
    {
        FavoriteDialogIsUpdate = update;
        if (!update)
        {
            FavoriteName = resetNameIfNew ?? string.Empty;
        }
        FavoriteError = null;
        ShowFavoriteDialog = true;
        RaiseStateChanged();
    }

    public void CloseFavoriteDialog()
    {
        ShowFavoriteDialog = false;
        RaiseStateChanged();
    }

    public async Task<FavoriteDto?> SubmitFavoriteDialogAsync(string defaultName, int primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool showChart, bool expandable, IReadOnlyCollection<int>? postingKinds, FiltersPayload? filters, CancellationToken ct = default)
    {
        FavoriteError = null;
        try
        {
            if (FavoriteDialogIsUpdate && ActiveFavoriteId.HasValue)
            {
                var res = await UpdateFavoriteAsync(ActiveFavoriteId.Value,
                    FavoriteName.Trim(), primaryKind, includeCategory, interval, take,
                    comparePrevious, compareYear, showChart, expandable, postingKinds, filters, ct);
                if (res is null)
                {
                    FavoriteError = "Error_UpdateFavorite";
                    RaiseStateChanged();
                    return null;
                }
                FavoriteName = res.Name;
                ShowFavoriteDialog = false;
                RaiseStateChanged();
                return res;
            }
            else
            {
                var name = string.IsNullOrWhiteSpace(FavoriteName) ? defaultName : FavoriteName.Trim();
                var res = await SaveFavoriteAsync(name, primaryKind, includeCategory, interval, take,
                    comparePrevious, compareYear, showChart, expandable, postingKinds, filters, ct);
                if (res is null)
                {
                    FavoriteError = "Error_SaveFavorite";
                    RaiseStateChanged();
                    return null;
                }
                ActiveFavoriteId = res.Id;
                FavoriteName = res.Name;
                ShowFavoriteDialog = false;
                RaiseStateChanged();
                return res;
            }
        }
        catch (Exception ex)
        {
            FavoriteError = ex.Message;
            RaiseStateChanged();
            return null;
        }
    }

    public sealed record FiltersPayload(
        IReadOnlyCollection<Guid>? AccountIds,
        IReadOnlyCollection<Guid>? ContactIds,
        IReadOnlyCollection<Guid>? SavingsPlanIds,
        IReadOnlyCollection<Guid>? SecurityIds,
        IReadOnlyCollection<Guid>? ContactCategoryIds,
        IReadOnlyCollection<Guid>? SavingsPlanCategoryIds,
        IReadOnlyCollection<Guid>? SecurityCategoryIds
    );

    public sealed record QueryRequest(int PostingKind, int Interval, int Take, bool IncludeCategory, bool ComparePrevious, bool CompareYear, IReadOnlyCollection<int>? PostingKinds, DateTime? AnalysisDate, FiltersPayload? Filters);

    public sealed record AggregationResponse(int Interval, List<PointDto> Points, bool ComparedPrevious, bool ComparedYear);
    public sealed record PointDto(DateTime PeriodStart, string GroupKey, string GroupName, string? CategoryName, decimal Amount, string? ParentGroupKey, decimal? PreviousAmount, decimal? YearAgoAmount);

    public sealed record FavoriteDto(Guid Id, string Name, int PostingKind, bool IncludeCategory, int Interval, int Take, bool ComparePrevious, bool CompareYear, bool ShowChart, bool Expandable, DateTime CreatedUtc, DateTime? ModifiedUtc, IReadOnlyCollection<int> PostingKinds, FavFiltersDto? Filters);

    public sealed class FavCreate
    {
        public string Name { get; set; } = string.Empty;
        public int PostingKind { get; set; }
        public bool IncludeCategory { get; set; }
        public int Interval { get; set; }
        public int Take { get; set; }
        public bool ComparePrevious { get; set; }
        public bool CompareYear { get; set; }
        public bool ShowChart { get; set; }
        public bool Expandable { get; set; }
        public IReadOnlyCollection<int>? PostingKinds { get; set; }
        public FavFiltersDto? Filters { get; set; }
    }

    public sealed class FavFiltersDto
    {
        public IReadOnlyCollection<Guid>? AccountIds { get; set; }
        public IReadOnlyCollection<Guid>? ContactIds { get; set; }
        public IReadOnlyCollection<Guid>? SavingsPlanIds { get; set; }
        public IReadOnlyCollection<Guid>? SecurityIds { get; set; }
        public IReadOnlyCollection<Guid>? ContactCategoryIds { get; set; }
        public IReadOnlyCollection<Guid>? SavingsPlanCategoryIds { get; set; }
        public IReadOnlyCollection<Guid>? SecurityCategoryIds { get; set; }
    }

    // Filter options (for dialog)
    public sealed class SimpleOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
    public bool FilterOptionsLoading { get; private set; }
    public Dictionary<int, List<SimpleOption>> FilterOptionsByKind { get; } = new();
    public int? ActiveFilterTabKind { get; set; }

    public async Task LoadFilterOptionsAsync(CancellationToken ct = default)
    {
        FilterOptionsLoading = true;
        FilterOptionsByKind.Clear();
        RaiseStateChanged();
        try
        {
            foreach (var k in SelectedKinds)
            {
                var kind = k; // PostingKind as int
                var list = new List<SimpleOption>();
                if (IncludeCategory && IsCategorySupported(kind))
                {
                    if (kind == 1) // Contact
                    {
                        var resp = await _http.GetAsync("/api/contact-categories", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var cats = await resp.Content.ReadFromJsonAsync<List<CategoryDto>>(cancellationToken: ct) ?? new();
                            list = cats.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                        }
                    }
                    else if (kind == 2) // SavingsPlan
                    {
                        var resp = await _http.GetAsync("/api/savings-plan-categories", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var cats = await resp.Content.ReadFromJsonAsync<List<CategoryDto>>(cancellationToken: ct) ?? new();
                            list = cats.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                        }
                    }
                    else if (kind == 3) // Security
                    {
                        var resp = await _http.GetAsync("/api/security-categories", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var cats = await resp.Content.ReadFromJsonAsync<List<CategoryDto>>(cancellationToken: ct) ?? new();
                            list = cats.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                        }
                    }
                }
                else
                {
                    if (kind == 0) // Bank
                    {
                        var resp = await _http.GetAsync("/api/accounts?skip=0&take=1000", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var acc = await resp.Content.ReadFromJsonAsync<List<EntityDto>>(cancellationToken: ct) ?? new();
                            list = acc.Select(a => new SimpleOption { Id = a.Id, Name = a.Name }).ToList();
                        }
                    }
                    else if (kind == 1) // Contact
                    {
                        var resp = await _http.GetAsync("/api/contacts?all=true", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var con = await resp.Content.ReadFromJsonAsync<List<EntityDto>>(cancellationToken: ct) ?? new();
                            list = con.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                        }
                    }
                    else if (kind == 2) // SavingsPlan
                    {
                        var resp = await _http.GetAsync("/api/savings-plans?onlyActive=false", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var sav = await resp.Content.ReadFromJsonAsync<List<EntityDto>>(cancellationToken: ct) ?? new();
                            list = sav.Select(p => new SimpleOption { Id = p.Id, Name = p.Name }).ToList();
                        }
                    }
                    else if (kind == 3) // Security
                    {
                        var resp = await _http.GetAsync("/api/securities?onlyActive=false", ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var sec = await resp.Content.ReadFromJsonAsync<List<EntityDto>>(cancellationToken: ct) ?? new();
                            list = sec.Select(s => new SimpleOption { Id = s.Id, Name = s.Name }).ToList();
                        }
                    }
                }
                FilterOptionsByKind[k] = list;
            }
        }
        finally
        {
            FilterOptionsLoading = false;
            RaiseStateChanged();
        }
    }

    public int GetActiveFilterTabKind()
    {
        if (ActiveFilterTabKind.HasValue && SelectedKinds.Contains(ActiveFilterTabKind.Value))
        {
            return ActiveFilterTabKind.Value;
        }
        return PrimaryKind;
    }
    public List<SimpleOption> GetOptionsForKind(int k)
        => FilterOptionsByKind.TryGetValue(k, out var list) ? list : new List<SimpleOption>();

    // Lightweight DTOs to fetch options
    private sealed class EntityDto { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }
    private sealed class CategoryDto { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }

    // Filter dialog state and temp buffers
    public bool ShowFilterDialog { get; set; }
    public HashSet<Guid> TempAccounts { get; private set; } = new();
    public HashSet<Guid> TempContacts { get; private set; } = new();
    public HashSet<Guid> TempSavings { get; private set; } = new();
    public HashSet<Guid> TempSecurities { get; private set; } = new();
    public HashSet<Guid> TempContactCats { get; private set; } = new();
    public HashSet<Guid> TempSavingsCats { get; private set; } = new();
    public HashSet<Guid> TempSecurityCats { get; private set; } = new();

    public void OpenFilterDialog()
    {
        TempAccounts = new HashSet<Guid>(SelectedAccounts);
        TempContacts = new HashSet<Guid>(SelectedContacts);
        TempSavings = new HashSet<Guid>(SelectedSavingsPlans);
        TempSecurities = new HashSet<Guid>(SelectedSecurities);
        TempContactCats = new HashSet<Guid>(SelectedContactCategories);
        TempSavingsCats = new HashSet<Guid>(SelectedSavingsCategories);
        TempSecurityCats = new HashSet<Guid>(SelectedSecurityCategories);
        ShowFilterDialog = true;
        ActiveFilterTabKind = SelectedKinds.FirstOrDefault();
        _ = LoadFilterOptionsAsync();
        RaiseStateChanged();
    }

    public void CloseFilterDialog()
    {
        ShowFilterDialog = false;
        RaiseStateChanged();
    }

    public bool IsOptionSelectedTemp(int kind, Guid id)
    {
        if (IncludeCategory && IsCategorySupported(kind))
        {
            return kind switch
            {
                1 => TempContactCats.Contains(id),
                2 => TempSavingsCats.Contains(id),
                3 => TempSecurityCats.Contains(id),
                _ => false
            };
        }
        return kind switch
        {
            0 => TempAccounts.Contains(id),
            1 => TempContacts.Contains(id),
            2 => TempSavings.Contains(id),
            3 => TempSecurities.Contains(id),
            _ => false
        };
    }

    public void ToggleTempForKind(int kind, Guid id, bool isChecked)
    {
        HashSet<Guid> set = IncludeCategory && IsCategorySupported(kind)
            ? kind switch
            {
                1 => TempContactCats,
                2 => TempSavingsCats,
                3 => TempSecurityCats,
                _ => TempAccounts
            }
            : kind switch
            {
                0 => TempAccounts,
                1 => TempContacts,
                2 => TempSavings,
                3 => TempSecurities,
                _ => TempAccounts
            };
        if (isChecked) { set.Add(id); } else { set.Remove(id); }
        RaiseStateChanged();
    }

    public int GetSelectedTempFiltersCount()
    {
        if (IsMulti)
        {
            var entityCount = TempAccounts.Count + TempContacts.Count + TempSavings.Count + TempSecurities.Count;
            var catCount = TempContactCats.Count + TempSavingsCats.Count + TempSecurityCats.Count;
            return IncludeCategory ? (catCount + TempAccounts.Count) : entityCount;
        }
        else
        {
            var kind = PrimaryKind;
            if (IncludeCategory && IsCategorySupported(PrimaryKind))
            {
                return kind switch
                {
                    1 => TempContactCats.Count,
                    2 => TempSavingsCats.Count,
                    3 => TempSecurityCats.Count,
                    _ => 0
                };
            }
            else
            {
                return kind switch
                {
                    0 => TempAccounts.Count,
                    1 => TempContacts.Count,
                    2 => TempSavings.Count,
                    3 => TempSecurities.Count,
                    _ => 0
                };
            }
        }
    }

    public void ClearTempFilters()
    {
        TempAccounts.Clear();
        TempContacts.Clear();
        TempSavings.Clear();
        TempSecurities.Clear();
        TempContactCats.Clear();
        TempSavingsCats.Clear();
        TempSecurityCats.Clear();
        RaiseStateChanged();
    }

    public async Task ApplyTempAndReloadAsync(DateTime? analysisDate, CancellationToken ct = default)
    {
        SelectedAccounts.Clear(); foreach (var id in TempAccounts) { SelectedAccounts.Add(id); }
        SelectedContacts.Clear(); foreach (var id in TempContacts) { SelectedContacts.Add(id); }
        SelectedSavingsPlans.Clear(); foreach (var id in TempSavings) { SelectedSavingsPlans.Add(id); }
        SelectedSecurities.Clear(); foreach (var id in TempSecurities) { SelectedSecurities.Add(id); }
        SelectedContactCategories.Clear(); foreach (var id in TempContactCats) { SelectedContactCategories.Add(id); }
        SelectedSavingsCategories.Clear(); foreach (var id in TempSavingsCats) { SelectedSavingsCategories.Add(id); }
        SelectedSecurityCategories.Clear(); foreach (var id in TempSecurityCats) { SelectedSecurityCategories.Add(id); }
        ShowFilterDialog = false;
        await ReloadAsync(analysisDate, ct);
    }

    public int GetSelectedFiltersCount()
    {
        if (IsMulti)
        {
            var entityCount = SelectedAccounts.Count + SelectedContacts.Count + SelectedSavingsPlans.Count + SelectedSecurities.Count;
            var catCount = SelectedContactCategories.Count + SelectedSavingsCategories.Count + SelectedSecurityCategories.Count;
            return IncludeCategory ? (catCount + SelectedAccounts.Count) : entityCount;
        }
        else
        {
            var kind = PrimaryKind;
            if (IncludeCategory && IsCategorySupported(PrimaryKind))
            {
                return kind switch
                {
                    1 => SelectedContactCategories.Count,
                    2 => SelectedSavingsCategories.Count,
                    3 => SelectedSecurityCategories.Count,
                    _ => 0
                };
            }
            else
            {
                return kind switch
                {
                    0 => SelectedAccounts.Count,
                    1 => SelectedContacts.Count,
                    2 => SelectedSavingsPlans.Count,
                    3 => SelectedSecurities.Count,
                    _ => 0
                };
            }
        }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var nav = new UiRibbonGroup(localizer["Ribbon_Navigation"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_BackToOverview"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        });
        var actions = new UiRibbonGroup(localizer["Ribbon_ReportActions"], new List<UiRibbonItem>
        {
            new UiRibbonItem(EditMode ? localizer["Ribbon_View"] : localizer["Ribbon_Edit"], EditMode? "<svg><use href='/icons/sprite.svg#eye'/></svg>":"<svg><use href='/icons/sprite.svg#edit'/></svg>", UiRibbonItemSize.Large, false, "ToggleEdit"),
            new UiRibbonItem(localizer["Ribbon_Save"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Small, !EditMode, "Save"),
            new UiRibbonItem(localizer["Ribbon_SaveAs"], "<svg><use href='/icons/sprite.svg#save-as'/></svg>", UiRibbonItemSize.Small, !EditMode, "SaveAs"),
            new UiRibbonItem(localizer["Ribbon_DeleteFavorite"], "<svg><use href='/icons/sprite.svg#trash'/></svg>", UiRibbonItemSize.Small, !ActiveFavoriteId.HasValue, "DeleteFavorite"),
            new UiRibbonItem(localizer["Ribbon_ReloadData"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, false, "Reload")
        });
        var filters = new UiRibbonGroup(localizer["Ribbon_Filter"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_OpenFilters"], "<svg><use href='/icons/sprite.svg#filters'/></svg>", UiRibbonItemSize.Small, FilterOptionsLoading || !EditMode, "FiltersOpen"),
            new UiRibbonItem(localizer["Ribbon_ClearFilters"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, GetSelectedFiltersCount()==0, "FiltersClear")
        });
        return new List<UiRibbonGroup> { nav, actions, filters };
    }
}
