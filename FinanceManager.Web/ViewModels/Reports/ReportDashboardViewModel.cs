using FinanceManager.Shared; // IApiClient
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Reports;

/// <summary>
/// View model for the reports dashboard. Manages dashboard state, filters, favorites and
/// communicates with the API to load aggregates for chart/table display.
/// </summary>
public sealed class ReportDashboardViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    /// <summary>
    /// Initializes a new instance of <see cref="ReportDashboardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services such as the API client.</param>
    public ReportDashboardViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
    }

    // UI state moved from component

    /// <summary>
    /// When true the dashboard is in edit mode allowing changes to filters and favorites.
    /// </summary>
    public bool EditMode { get; set; }

    // Dashboard state

    /// <summary>
    /// Indicates whether a background load operation is in progress.
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Selected posting kinds to include in the report (e.g. Bank, Contact, Security).
    /// </summary>
    /// <returns>The result.</returns>
    public List<PostingKind> SelectedKinds { get; set; } = new() { PostingKind.Bank };

    /// <summary>
    /// Selected reporting interval stored as integer mapped to <see cref="ReportInterval"/>.
    /// </summary>
    /// <returns>The result.</returns>
    public int Interval { get; set; }
        = (int)ReportInterval.Month;

    /// <summary>
    /// When true include category grouping in the aggregates.
    /// </summary>
    public bool IncludeCategory { get; set; }

    /// <summary>
    /// When true compare to previous period values.
    /// </summary>
    public bool ComparePrevious { get; set; }

    /// <summary>
    /// When true compare to year-ago values.
    /// </summary>
    public bool CompareYear { get; set; }

    /// <summary>
    /// When true request dividend projection amounts.
    /// </summary>
    public bool CompareProjection { get; set; }

    /// <summary>
    /// True when the last server result contains projection amounts.
    /// </summary>
    public bool ComparedProjection { get; private set; }

    /// <summary>
    /// Controls whether the chart view is shown.
    /// </summary>
    public bool ShowChart { get; set; } = true;

    /// <summary>
    /// Number of periods to take when querying aggregates.
    /// </summary>
    public int Take { get; set; } = 24;

    /// <summary>
    /// When true include dividend-related postings in the results.
    /// </summary>
    public bool IncludeDividendRelated { get; set; }

    /// <summary>
    /// When true use Valuta date instead of booking date when computing aggregates.
    /// </summary>
    public bool UseValutaDate { get; set; }

    /// <summary>
    /// Active favorite id currently selected or <c>null</c> when none.
    /// </summary>
    public Guid? ActiveFavoriteId { get; set; }

    /// <summary>
    /// Name of the current favorite in the favorite dialog or UI.
    /// </summary>
    public string FavoriteName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the favorite dialog is visible.
    /// </summary>
    public bool ShowFavoriteDialog { get; private set; }

    /// <summary>
    /// Indicates whether the favorite dialog is currently in update mode (true) or create mode (false).
    /// </summary>
    public bool FavoriteDialogIsUpdate { get; private set; }

    /// <summary>
    /// Last error key or message related to favorite operations, or <c>null</c> when none.
    /// </summary>
    public string? FavoriteError { get; private set; }

    // Filters state (entity level)

    /// <summary>Selected account ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedAccounts { get; private set; } = new();
    /// <summary>Selected contact ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedContacts { get; private set; } = new();
    /// <summary>Selected savings plan ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedSavingsPlans { get; private set; } = new();
    /// <summary>Selected security ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedSecurities { get; private set; } = new();
    // Filters state (category level)
    /// <summary>Selected contact category ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedContactCategories { get; private set; } = new();
    /// <summary>Selected savings plan category ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedSavingsCategories { get; private set; } = new();
    /// <summary>Selected security category ids.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> SelectedSecurityCategories { get; private set; } = new();
    // New: security posting subtype filter (by enum int values)
    /// <summary>Selected security subtype values (enum int representation).</summary>
    /// <returns>The result.</returns>
    public HashSet<int> SelectedSecuritySubTypes { get; private set; } = new();

    /// <summary>
    /// Aggregated result points returned from the API and used for rendering table and chart.
    /// </summary>
    /// <returns>The result.</returns>
    public List<ReportAggregatePointDto> Points { get; private set; } = new();

    // Expansion state for table rows
    /// <summary>Expansion state dictionary for row grouping keys.</summary>
    /// <returns>The result.</returns>
    public Dictionary<string, bool> Expanded { get; } = new();

    /// <summary>
    /// Toggles the expansion state for the specified grouping key and raises state changed.
    /// </summary>
    /// <param name="key">Grouping key to toggle.</param>
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

    /// <summary>
    /// Returns whether the specified grouping key is expanded.
    /// </summary>
    /// <param name="key">Grouping key.</param>
    /// <returns><c>true</c> when expanded; otherwise <c>false</c>.</returns>
    public bool IsExpanded(string key)
        => Expanded.TryGetValue(key, out var v) && v;

    /// <summary>
    /// Primary kind derived from currently selected kinds (first entry).
    /// </summary>
    /// <returns>The result.</returns>
    public PostingKind PrimaryKind => SelectedKinds.FirstOrDefault();

    /// <summary>
    /// Indicates that multiple posting kinds are selected.
    /// </summary>
    public bool IsMulti => SelectedKinds.Count > 1;

    /// <summary>
    /// True when the current selection contains only securities.
    /// </summary>
    public bool IsSecurityOnlySelection => SelectedKinds.Count == 1 && SelectedKinds[0] == PostingKind.Security;

    /// <summary>
    /// True when projection can be requested for the current UI state.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool CanCompareProjection
    {
        get
        {
            return IsSecurityOnlySelection && (ReportInterval)Interval != ReportInterval.AllHistory;
        }
    }

    /// <summary>
    /// Returns whether the provided posting kind supports category grouping.
    /// </summary>
    /// <param name="kind">Posting kind.</param>
    /// <returns><c>true</c> when category grouping is supported for the supplied kind.</returns>
    /// <remarks>
    /// Supported enum values: <see cref="PostingKind.Contact"/>, <see cref="PostingKind.SavingsPlan"/>, <see cref="PostingKind.Security"/>.
    /// </remarks>
    public static bool IsCategorySupported(PostingKind kind) => kind == PostingKind.Contact || kind == PostingKind.SavingsPlan || kind == PostingKind.Security;

    /// <summary>
    /// True when category grouping is enabled and applicable for the primary kind.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool IsCategoryGroupingSingle
    {
        get
        {
            return !IsMulti && IncludeCategory && IsCategorySupported(PrimaryKind);
        }
    }

    /// <summary>
    /// Latest aggregate point per non-hierarchical group used to display summary rows.
    /// </summary>
    public IReadOnlyList<ReportAggregatePointDto> LatestPerGroup => Points
        .Where(p => !p.GroupKey.StartsWith("Type:") && !p.GroupKey.StartsWith("Category:") && p.ParentGroupKey == null)
        .GroupBy(p => p.GroupKey)
        .Select(g => g.OrderBy(x => x.PeriodStart).Last())
        .OrderBy(p => IsNegative(p))
        .ThenByDescending(p => p.Amount)
        .ToList();

    /// <summary>
    /// Builds the filters payload to send to the report aggregates endpoint based on the current filter state.
    /// Returns <c>null</c> when no meaningful filters are set and the request would be equivalent to an unfiltered query.
    /// </summary>
    /// <returns>A <see cref="ReportAggregatesFiltersRequest"/> or <c>null</c> when no filters should be applied.</returns>
    public ReportAggregatesFiltersRequest? BuildFiltersPayload()
    {
        var hasEntity = SelectedAccounts.Count > 0 || SelectedContacts.Count > 0 || SelectedSavingsPlans.Count > 0 || SelectedSecurities.Count > 0;
        var hasCats = SelectedContactCategories.Count > 0 || SelectedSavingsCategories.Count > 0 || SelectedSecurityCategories.Count > 0 || SelectedAccounts.Count > 0;
        var hasSecTypes = SelectedSecuritySubTypes.Count > 0;
        var includeDiv = IncludeDividendRelated;
        if (!IncludeCategory && !hasEntity && !hasSecTypes && !includeDiv)
        {
            return null;
        }
        if (IncludeCategory && !hasCats && !hasSecTypes && !includeDiv)
        {
            return null;
        }
        if (IncludeCategory)
        {
            return new ReportAggregatesFiltersRequest(
                SelectedAccounts.ToList(),
                null,
                null,
                null,
                SelectedContactCategories.ToList(),
                SelectedSavingsCategories.ToList(),
                SelectedSecurityCategories.ToList(),
                SelectedSecuritySubTypes.ToList(),
                includeDiv
            );
        }
        else
        {
            return new ReportAggregatesFiltersRequest(
                SelectedAccounts.ToList(),
                SelectedContacts.ToList(),
                SelectedSavingsPlans.ToList(),
                SelectedSecurities.ToList(),
                null,
                null,
                null,
                SelectedSecuritySubTypes.ToList(),
                includeDiv
            );
        }
    }

    /// <summary>
    /// Clears all applied filters and resets filter-related flags.
    /// </summary>
    public void ClearFilters()
    {
        SelectedAccounts.Clear();
        SelectedContacts.Clear();
        SelectedSavingsPlans.Clear();
        SelectedSecurities.Clear();
        SelectedContactCategories.Clear();
        SelectedSavingsCategories.Clear();
        SelectedSecurityCategories.Clear();
        SelectedSecuritySubTypes.Clear();
        IncludeDividendRelated = false;
    }

    /// <summary>
    /// Reloads aggregates from the API using the current dashboard state.
    /// </summary>
    /// <param name="analysisDate">Optional date used as the analysis reference point.</param>
    /// <param name="ct">Cancellation token used to cancel the load operation.</param>
    /// <returns>A task that completes when reload has finished.</returns>
    public async Task ReloadAsync(DateTime? analysisDate, CancellationToken ct = default)
    {
        if (IsBusy) { return; }
        IsBusy = true; RaiseStateChanged();
        try
        {
            if (!CanCompareProjection)
            {
                CompareProjection = false;
            }
            var result = await LoadAsync(PrimaryKind, Interval, Take, IncludeCategory, ComparePrevious, CompareYear, CompareProjection, IsMulti ? SelectedKinds : null, analysisDate, BuildFiltersPayload(), UseValutaDate, ct);
            ComparedProjection = result.ComparedProjection;
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

    /// <summary>
    /// Returns the top-level rows for the table view depending on current grouping and selection mode.
    /// </summary>
    /// <returns>Sequence of top-level <see cref="ReportAggregatePointDto"/> rows.</returns>
    public IEnumerable<ReportAggregatePointDto> GetTopLevelRows()
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

    /// <summary>
    /// Returns child rows for the specified parent grouping key.
    /// </summary>
    /// <param name="parentKey">Parent grouping key.</param>
    /// <returns>Sequence of child <see cref="ReportAggregatePointDto"/> rows.</returns>
    public IEnumerable<ReportAggregatePointDto> GetChildRows(string parentKey)
        => GetChildRowsImpl(parentKey);

    private IEnumerable<ReportAggregatePointDto> GetChildRowsImpl(string parentKey)
    {
        if (parentKey.StartsWith("Type:"))
        {
            var kindName = parentKey.Substring("Type:".Length);
            // decide per type whether category children are supported
            PostingKind typeKind = kindName switch
            {
                "Bank" => PostingKind.Bank,
                "Contact" => PostingKind.Contact,
                "SavingsPlan" => PostingKind.SavingsPlan,
                "Security" => PostingKind.Security,
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
        return Array.Empty<ReportAggregatePointDto>();
    }

    /// <summary>
    /// Returns whether a grouping key has child rows.
    /// </summary>
    /// <param name="key">Grouping key.</param>
    /// <returns><c>true</c> when children exist.</returns>
    public bool HasChildren(string key)
        => GetChildRowsImpl(key).Any();

    // Derived UI helpers
    /// <summary>
    /// Whether previous columns should be shown based on current settings.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool ShowPreviousColumns => ComparePrevious && ((ReportInterval)Interval is not ReportInterval.Year and not ReportInterval.Ytd);
    /// <summary>
    /// Whether the category column should be shown in the table.
    /// </summary>
    public bool ShowCategoryColumn => IncludeCategory && !IsCategoryGroupingSingle;

    /// <summary>
    /// Whether the projection column should be shown in the table.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool ShowProjectionColumn
    {
        get
        {
            return ComparedProjection || (CompareProjection && CanCompareProjection);
        }
    }

    /// <summary>
    /// Computes totals for the currently visible top-level rows including optional previous/year comparisons.
    /// </summary>
    /// <returns>Tuple with current sum, previous sum (or <c>null</c>) and year-ago sum (or <c>null</c>).</returns>
    public ReportTotals GetTotals()
    {
        var rows = GetTopLevelRows().ToList();
        var amount = rows.Sum(r => r.Amount);
        decimal? projection = ShowProjectionColumn ? rows.Where(r => r.ProjectionAmount.HasValue).Sum(r => r.ProjectionAmount!.Value) : null;
        decimal? prev = ShowPreviousColumns ? rows.Where(r => r.PreviousAmount.HasValue).Sum(r => r.PreviousAmount!.Value) : null;
        decimal? year = CompareYear ? rows.Where(r => r.YearAgoAmount.HasValue).Sum(r => r.YearAgoAmount!.Value) : null;
        return new ReportTotals(amount, projection, prev, year);
    }

    /// <summary>
    /// Returns an ordered sequence of period sums for charting.
    /// </summary>
    /// <returns>List of period start and sum tuples.</returns>
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

    /// <summary>
    /// Determines whether the provided aggregate point should be considered negative for ordering/visualization.
    /// </summary>
    /// <param name="p">Aggregate point to evaluate.</param>
    /// <returns><c>true</c> when negative according to heuristic rules.</returns>
    public static bool IsNegative(ReportAggregatePointDto p)
    {
        if (p.Amount < 0m)
        {
            return true;
        }
        if (p.Amount == 0m)
        {
            var hasProjection = p.ProjectionAmount.HasValue;
            var hasPrev = p.PreviousAmount.HasValue;
            var hasYear = p.YearAgoAmount.HasValue;
            if (hasProjection || hasPrev || hasYear)
            {
                var projectionNeg = hasProjection && p.ProjectionAmount!.Value < 0m;
                var prevNeg = hasPrev && p.PreviousAmount!.Value < 0m;
                var yearNeg = hasYear && p.YearAgoAmount!.Value < 0m;
                if ((!hasProjection || projectionNeg) && (!hasPrev || prevNeg) && (!hasYear || yearNeg))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Loads aggregates from the API for the provided parameters.
    /// </summary>
    /// <param name="primaryKind">Primary posting kind.</param>
    /// <param name="interval">Interval integer mapped to <see cref="ReportInterval"/>.</param>
    /// <param name="take">Number of periods to take.</param>
    /// <param name="includeCategory">Whether to include category grouping.</param>
    /// <param name="comparePrevious">Whether to compute previous period comparisons.</param>
    /// <param name="compareYear">Whether to compute year-ago comparisons.</param>
    /// <param name="compareProjection">Whether to compute projected-period comparisons.</param>
    /// <param name="postingKinds">Optional set of posting kinds when multi-kind selection is used.</param>
    /// <param name="analysisDate">Optional analysis date used by the server.</param>
    /// <param name="filters">Optional filters payload.</param>
    /// <param name="useValutaDate">Whether to use Valuta date instead of booking date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ReportAggregationResult"/> containing aggregated points and metadata.</returns>
    public async Task<ReportAggregationResult> LoadAsync(PostingKind primaryKind, int interval, int take, bool includeCategory, bool comparePrevious, bool compareYear, bool compareProjection, IReadOnlyCollection<PostingKind>? postingKinds, DateTime? analysisDate, ReportAggregatesFiltersRequest? filters, bool useValutaDate = false, CancellationToken ct = default)
    {
        var req = new ReportAggregatesQueryRequest(primaryKind, (ReportInterval)interval, take, includeCategory, comparePrevious, compareYear, compareProjection, useValutaDate, postingKinds, analysisDate, filters);
        var result = await _api.Reports_QueryAggregatesAsync(req, ct);
        return result ?? new ReportAggregationResult((ReportInterval)interval, Array.Empty<ReportAggregatePointDto>(), false, false, false);
    }

    /// <summary>
    /// Saves a report favorite via the API.
    /// </summary>
    /// <param name="name">Favorite name.</param>
    /// <param name="primaryKind">Primary posting kind.</param>
    /// <param name="includeCategory">Whether category grouping is included.</param>
    /// <param name="interval">Interval value.</param>
    /// <param name="take">Number of periods to take.</param>
    /// <param name="comparePrevious">Whether previous comparison is enabled.</param>
    /// <param name="compareYear">Whether year comparison is enabled.</param>
    /// <param name="compareProjection">Whether projected-period comparison is enabled.</param>
    /// <param name="showChart">Whether chart is shown for this favorite.</param>
    /// <param name="expandable">Whether table rows are expandable.</param>
    /// <param name="postingKinds">Optional posting kinds when multi-kind selection is used.</param>
    /// <param name="filters">Optional filters payload.</param>
    /// <param name="useValutaDate">Whether to use Valuta date for this favorite.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="ReportFavoriteDto"/> or <c>null</c> when creation failed.</returns>
    public async Task<ReportFavoriteDto?> SaveFavoriteAsync(string name, PostingKind primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool compareProjection, bool showChart, bool expandable, IReadOnlyCollection<PostingKind>? postingKinds, ReportAggregatesFiltersRequest? filters, bool useValutaDate = false, CancellationToken ct = default)
    {
        var payload = new ReportFavoriteCreateApiRequest
        {
            Name = name,
            PostingKind = primaryKind,
            IncludeCategory = includeCategory,
            Interval = interval,
            Take = take,
            ComparePrevious = comparePrevious,
            CompareYear = compareYear,
            CompareProjection = compareProjection,
            ShowChart = showChart,
            Expandable = expandable,
            PostingKinds = postingKinds,
            Filters = filters is null ? null : new ReportFavoriteFiltersDto(
                filters.AccountIds,
                filters.ContactIds,
                filters.SavingsPlanIds,
                filters.SecurityIds,
                filters.ContactCategoryIds,
                filters.SavingsPlanCategoryIds,
                filters.SecurityCategoryIds,
                filters.SecuritySubTypes,
                filters.IncludeDividendRelated
            ),
            UseValutaDate = useValutaDate
        };
        return await _api.Reports_CreateFavoriteAsync(payload, ct);
    }

    /// <summary>
    /// Updates an existing report favorite via the API.
    /// </summary>
    /// <param name="id">Favorite id to update.</param>
    /// <param name="name">Favorite name.</param>
    /// <param name="primaryKind">Primary posting kind.</param>
    /// <param name="includeCategory">Whether category grouping is included.</param>
    /// <param name="interval">Interval value.</param>
    /// <param name="take">Number of periods to take.</param>
    /// <param name="comparePrevious">Whether previous comparison is enabled.</param>
    /// <param name="compareYear">Whether year comparison is enabled.</param>
    /// <param name="compareProjection">Whether projected-period comparison is enabled.</param>
    /// <param name="showChart">Whether chart is shown for this favorite.</param>
    /// <param name="expandable">Whether table rows are expandable.</param>
    /// <param name="postingKinds">Optional posting kinds when multi-kind selection is used.</param>
    /// <param name="filters">Optional filters payload.</param>
    /// <param name="useValutaDate">Whether to use Valuta date for this favorite.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="ReportFavoriteDto"/> or <c>null</c> when update failed.</returns>
    public async Task<ReportFavoriteDto?> UpdateFavoriteAsync(Guid id, string name, PostingKind primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool compareProjection, bool showChart, bool expandable, IReadOnlyCollection<PostingKind>? postingKinds, ReportAggregatesFiltersRequest? filters, bool useValutaDate = false, CancellationToken ct = default)
    {
        var payload = new ReportFavoriteUpdateApiRequest
        {
            Name = name,
            PostingKind = primaryKind,
            IncludeCategory = includeCategory,
            Interval = interval,
            Take = take,
            ComparePrevious = comparePrevious,
            CompareYear = compareYear,
            CompareProjection = compareProjection,
            ShowChart = showChart,
            Expandable = expandable,
            PostingKinds = postingKinds,
            Filters = filters is null ? null : new ReportFavoriteFiltersDto(
                filters.AccountIds,
                filters.ContactIds,
                filters.SavingsPlanIds,
                filters.SecurityIds,
                filters.ContactCategoryIds,
                filters.SavingsPlanCategoryIds,
                filters.SecurityCategoryIds,
                filters.SecuritySubTypes,
                filters.IncludeDividendRelated
            ),
            UseValutaDate = useValutaDate
        };
        return await _api.Reports_UpdateFavoriteAsync(id, payload, ct);
    }

    /// <summary>
    /// Deletes a report favorite by id via the API.
    /// </summary>
    /// <param name="id">Favorite id to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when deletion succeeded.</returns>
    public async Task<bool> DeleteFavoriteAsync(Guid id, CancellationToken ct = default)
    {
        return await _api.Reports_DeleteFavoriteAsync(id, ct);
    }

    // Filter options (for dialog)
    /// <summary>
    /// Simple option DTO used for filter dialog select lists.
    /// </summary>
    public sealed class SimpleOption
    {
        /// <summary>Option identifier.</summary>
        public Guid Id { get; set; }
        /// <summary>Option display name.</summary>
        public string Name { get; set; } = string.Empty;
    }
    /// <summary>Indicates whether filter options are loading.</summary>
    public bool FilterOptionsLoading { get; private set; }
    /// <summary>Map of available filter options per posting kind.</summary>
    /// <returns>The result.</returns>
    public Dictionary<PostingKind, List<SimpleOption>> FilterOptionsByKind { get; } = new();
    /// <summary>Active filter tab kind in the filter dialog.</summary>
    public PostingKind? ActiveFilterTabKind { get; set; }

    /// <summary>
    /// Loads available filter options for the kinds selected in the dashboard (populates <see cref="FilterOptionsByKind"/>).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadFilterOptionsAsync(CancellationToken ct = default)
    {
        FilterOptionsLoading = true;
        FilterOptionsByKind.Clear();
        RaiseStateChanged();
        try
        {
            foreach (var k in SelectedKinds)
            {
                var kind = k; // PostingKind as enum
                var list = new List<SimpleOption>();
                if (IncludeCategory && IsCategorySupported(kind))
                {
                    if (kind == PostingKind.Contact) // Contact
                    {
                        var cats = await _api.ContactCategories_ListAsync(ct);
                        list = cats.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                    }
                    else if (kind == PostingKind.SavingsPlan) // SavingsPlan
                    {
                        try
                        {
                            var sav = await _api.SavingsPlans_ListAsync(onlyActive: false, ct);
                            list = sav.Select(p => new SimpleOption { Id = p.Id, Name = p.Name }).ToList();
                        }
                        catch
                        {
                            list = new List<SimpleOption>();
                        }
                    }
                    else if (kind == PostingKind.Security) // Security
                    {
                        var cats = await _api.SecurityCategories_ListAsync(ct);
                        list = cats.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                    }
                }
                else
                {
                    if (kind == PostingKind.Bank) // Bank
                    {
                        var acc = await _api.GetAccountsAsync(skip: 0, take: 1000, bankContactId: null, q: null, ct);
                        list = acc.Select(a => new SimpleOption { Id = a.Id, Name = a.Name }).ToList();
                    }
                    else if (kind == PostingKind.Contact) // Contact
                    {
                        var con = await _api.Contacts_ListAsync(skip: 0, take: 1000, type: null, all: true, nameFilter: null, ct);
                        list = con.Select(c => new SimpleOption { Id = c.Id, Name = c.Name }).ToList();
                    }
                    else if (kind == PostingKind.SavingsPlan) // SavingsPlan
                    {
                        try
                        {
                            var sav = await _api.SavingsPlans_ListAsync(onlyActive: false, ct);
                            list = sav.Select(p => new SimpleOption { Id = p.Id, Name = p.Name }).ToList();
                        }
                        catch
                        {
                            list = new List<SimpleOption>();
                        }
                    }
                    else if (kind == PostingKind.Security) // Security
                    {
                        var sec = await _api.Securities_ListAsync(onlyActive: false, ct);
                        list = sec.Select(s => new SimpleOption { Id = s.Id, Name = s.Name }).ToList();
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

    // Filter dialog state and temp buffers
    /// <summary>Indicates whether the filter dialog is shown.</summary>
    public bool ShowFilterDialog { get; set; }

    /// <summary>
    /// Temporary selected account ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempAccounts { get; private set; } = new();

    /// <summary>
    /// Temporary selected contact ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempContacts { get; private set; } = new();

    /// <summary>Indicates the temporary selected savings plan ids used in the filter dialog.</summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempSavings { get; private set; } = new();

    /// <summary>
    /// Temporary selected security ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempSecurities { get; private set; } = new();

    /// <summary>
    /// Temporary selected contact category ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempContactCats { get; private set; } = new();

    /// <summary>
    /// Temporary selected savings plan category ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempSavingsCats { get; private set; } = new();

    /// <summary>
    /// Temporary selected security category ids used in the filter dialog before applying changes.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<Guid> TempSecurityCats { get; private set; } = new();

    /// <summary>
    /// Temporary selected security subtype values (enum int representation) used in the filter dialog.
    /// </summary>
    /// <returns>The result.</returns>
    public HashSet<int> TempSecuritySubTypes { get; private set; } = new();

    /// <summary>
    /// Opens the filter dialog and prepares temporary buffers with current selections.
    /// </summary>
    public void OpenFilterDialog()
    {
        TempAccounts = new HashSet<Guid>(SelectedAccounts);
        TempContacts = new HashSet<Guid>(SelectedContacts);
        TempSavings = new HashSet<Guid>(SelectedSavingsPlans);
        TempSecurities = new HashSet<Guid>(SelectedSecurities);
        TempContactCats = new HashSet<Guid>(SelectedContactCategories);
        TempSavingsCats = new HashSet<Guid>(SelectedSavingsCategories);
        TempSecurityCats = new HashSet<Guid>(SelectedSecurityCategories);
        TempSecuritySubTypes = new HashSet<int>(SelectedSecuritySubTypes);
        ShowFilterDialog = true;
        ActiveFilterTabKind = SelectedKinds.FirstOrDefault();
        _ = LoadFilterOptionsAsync();
        RaiseStateChanged();
    }

    /// <summary>
    /// Closes the filter dialog without applying temporary changes.
    /// </summary>
    public void CloseFilterDialog()
    {
        ShowFilterDialog = false;
        RaiseStateChanged();
    }

    /// <summary>
    /// Returns whether a temporary option is selected for the specified posting kind and identifier.
    /// </summary>
    /// <param name="kind">Posting kind.</param>
    /// <param name="id">Identifier of the option.</param>
    /// <returns><c>true</c> when the option is selected in the temporary buffer; otherwise <c>false</c>.</returns>
    public bool IsOptionSelectedTemp(PostingKind kind, Guid id)
    {
        if (IncludeCategory && IsCategorySupported(kind))
        {
            return kind switch
            {
                PostingKind.Contact => TempContactCats.Contains(id),
                PostingKind.SavingsPlan => TempSavingsCats.Contains(id),
                PostingKind.Security => TempSecurityCats.Contains(id),
                _ => false
            };
        }
        return kind switch
        {
            PostingKind.Bank => TempAccounts.Contains(id),
            PostingKind.Contact => TempContacts.Contains(id),
            PostingKind.SavingsPlan => TempSavings.Contains(id),
            PostingKind.Security => TempSecurities.Contains(id),
            _ => false
        };
    }

    /// <summary>
    /// Toggles a temporary filter option for a kind and id.
    /// </summary>
    /// <param name="kind">Posting kind.</param>
    /// <param name="id">Identifier of the option.</param>
    /// <param name="isChecked">True to select, false to unselect.</param>
    public void ToggleTempForKind(PostingKind kind, Guid id, bool isChecked)
    {
        HashSet<Guid> set = IncludeCategory && IsCategorySupported(kind)
            ? kind switch
            {
                PostingKind.Contact => TempContactCats,
                PostingKind.SavingsPlan => TempSavingsCats,
                PostingKind.Security => TempSecurityCats,
                _ => TempAccounts
            }
            : kind switch
            {
                PostingKind.Bank => TempAccounts,
                PostingKind.Contact => TempContacts,
                PostingKind.SavingsPlan => TempSavings,
                PostingKind.Security => TempSecurities,
                _ => TempAccounts
            };
        if (isChecked) { set.Add(id); } else { set.Remove(id); }
        RaiseStateChanged();
    }

    /// <summary>
    /// Returns the active filter tab kind; falls back to <see cref="PrimaryKind"/> when the active tab is not present in selection.
    /// </summary>
    /// <returns>Posting kind used for active filter tab.</returns>
    public PostingKind GetActiveFilterTabKind()
    {
        if (ActiveFilterTabKind.HasValue && SelectedKinds.Contains(ActiveFilterTabKind.Value))
        {
            return ActiveFilterTabKind.Value;
        }
        return PrimaryKind;
    }

    /// <summary>
    /// Returns the available simple option list for a given posting kind.
    /// </summary>
    /// <param name="k">Posting kind to retrieve options for.</param>
    /// <returns>List of <see cref="SimpleOption"/> items for the supplied kind; empty list when none are available.</returns>
    public List<SimpleOption> GetOptionsForKind(PostingKind k)
        => FilterOptionsByKind.TryGetValue(k, out var list) ? list : new List<SimpleOption>();

    /// <summary>
    /// Clears temporary filter selections.
    /// </summary>
    public void ClearTempFilters()
    {
        TempAccounts.Clear();
        TempContacts.Clear();
        TempSavings.Clear();
        TempSecurities.Clear();
        TempContactCats.Clear();
        TempSavingsCats.Clear();
        TempSecurityCats.Clear();
        TempSecuritySubTypes.Clear();
        RaiseStateChanged();
    }

    /// <summary>
    /// Returns the number of temporary filters currently selected.
    /// </summary>
    /// <returns>Count of selected temporary filters.</returns>
    public int GetSelectedTempFiltersCount()
    {
        if (IsMulti)
        {
            var entityCount = TempAccounts.Count + TempContacts.Count + TempSavings.Count + TempSecurities.Count;
            var catCount = TempContactCats.Count + TempSavingsCats.Count + TempSecurityCats.Count;
            var typeCount = TempSecuritySubTypes.Count;
            return IncludeCategory ? (catCount + TempAccounts.Count + typeCount) : (entityCount + typeCount);
        }
        else
        {
            var kind = PrimaryKind;
            var typeCount = TempSecuritySubTypes.Count;
            if (IncludeCategory && IsCategorySupported(PrimaryKind))
            {
                var baseCount = kind switch
                {
                    PostingKind.Contact => TempContactCats.Count,
                    PostingKind.SavingsPlan => TempSavingsCats.Count,
                    PostingKind.Security => TempSecurityCats.Count,
                    _ => 0
                };
                return baseCount + typeCount;
            }
            else
            {
                var baseCount = kind switch
                {
                    PostingKind.Bank => TempAccounts.Count,
                    PostingKind.Contact => TempContacts.Count,
                    PostingKind.SavingsPlan => TempSavings.Count,
                    PostingKind.Security => TempSecurities.Count,
                    _ => 0
                };
                return baseCount + typeCount;
            }
        }
    }

    /// <summary>
    /// Applies temporary filters to the active filter sets and reloads the dashboard.
    /// </summary>
    /// <param name="analysisDate">Optional analysis date.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the applied filters have been applied and reload finished.</returns>
    public async Task ApplyTempAndReloadAsync(DateTime? analysisDate, CancellationToken ct = default)
    {
        SelectedAccounts.Clear(); foreach (var id in TempAccounts) { SelectedAccounts.Add(id); }
        SelectedContacts.Clear(); foreach (var id in TempContacts) { SelectedContacts.Add(id); }
        SelectedSavingsPlans.Clear(); foreach (var id in TempSavings) { SelectedSavingsPlans.Add(id); }
        SelectedSecurities.Clear(); foreach (var id in TempSecurities) { SelectedSecurities.Add(id); }
        SelectedContactCategories.Clear(); foreach (var id in TempContactCats) { SelectedContactCategories.Add(id); }
        SelectedSavingsCategories.Clear(); foreach (var id in TempSavingsCats) { SelectedSavingsCategories.Add(id); }
        SelectedSecurityCategories.Clear(); foreach (var id in TempSecurityCats) { SelectedSecurityCategories.Add(id); }
        SelectedSecuritySubTypes.Clear(); foreach (var t in TempSecuritySubTypes) { SelectedSecuritySubTypes.Add(t); }
        ShowFilterDialog = false;
        await ReloadAsync(analysisDate, ct);
    }

    // UI helpers for security subtypes in temp buffer
    /// <summary>
    /// Whether a temp security subtype is selected.
    /// </summary>
    /// <param name="subType">Security subtype value (enum int representation).</param>
    /// <returns><c>true</c> when selected in temp buffer.</returns>
    public bool IsSecuritySubTypeSelectedTemp(int subType)
        => TempSecuritySubTypes.Contains(subType);

    /// <summary>
    /// Toggles a temp security subtype selection and raises state changed.
    /// </summary>
    /// <param name="subType">Security subtype value (enum int representation).</param>
    /// <param name="isChecked">True to select; false to unselect.</param>
    public void ToggleTempSecuritySubType(int subType, bool isChecked)
    {
        if (isChecked) { TempSecuritySubTypes.Add(subType); }
        else { TempSecuritySubTypes.Remove(subType); }
        RaiseStateChanged();
    }

    /// <summary>
    /// Returns the total number of active filters currently selected.
    /// </summary>
    /// <returns>Count of active filters.</returns>
    public int GetSelectedFiltersCount()
    {
        if (IsMulti)
        {
            var entityCount = SelectedAccounts.Count + SelectedContacts.Count + SelectedSavingsPlans.Count + SelectedSecurities.Count;
            var catCount = SelectedContactCategories.Count + SelectedSavingsCategories.Count + SelectedSecurityCategories.Count;
            var typeCount = SelectedSecuritySubTypes.Count;
            return IncludeCategory ? (catCount + SelectedAccounts.Count + typeCount) : (entityCount + typeCount);
        }
        else
        {
            var kind = PrimaryKind;
            var typeCount = SelectedSecuritySubTypes.Count;
            if (IncludeCategory && IsCategorySupported(PrimaryKind))
            {
                var baseCount = kind switch
                {
                    PostingKind.Contact => SelectedContactCategories.Count,
                    PostingKind.SavingsPlan => SelectedSavingsCategories.Count,
                    PostingKind.Security => SelectedSecurityCategories.Count,
                    _ => 0
                };
                return baseCount + typeCount;
            }
            else
            {
                var baseCount = kind switch
                {
                    PostingKind.Bank => SelectedAccounts.Count,
                    PostingKind.Contact => SelectedContacts.Count,
                    PostingKind.SavingsPlan => SelectedSavingsPlans.Count,
                    PostingKind.Security => SelectedSecurities.Count,
                    _ => 0
                };
                return baseCount + typeCount;
            }
        }
    }

    /// <summary>
    /// Aggregates ribbon registers for the report dashboard including navigation, actions and filters.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels for ribbon actions.</param>
    /// <returns>Ribbon register definitions or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var tabs = new List<UiRibbonTab>();

        // Navigation
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_BackToOverview"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, new Func<Task>(() => { RaiseUiActionRequested("Back"); return Task.CompletedTask; })) { MobileShortcut = true }
        }));

        // Actions
        tabs.Add(new UiRibbonTab(localizer["Ribbon_ReportActions"], new List<UiRibbonAction>
        {
            new UiRibbonAction("ToggleEdit", (EditMode ? localizer["Ribbon_View"].Value : localizer["Ribbon_Edit"].Value), EditMode? "<svg><use href='/icons/sprite.svg#eye'/></svg>":"<svg><use href='/icons/sprite.svg#edit'/></svg>", UiRibbonItemSize.Large, false, null, new Func<Task>(() => { RaiseUiActionRequested("ToggleEdit"); return Task.CompletedTask; })),
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Small, !EditMode, null, new Func<Task>(() => { RaiseUiActionRequested("Save"); return Task.CompletedTask; })) { MobileShortcut = true },
            new UiRibbonAction("SaveAs", localizer["Ribbon_SaveAs"].Value, "<svg><use href='/icons/sprite.svg#save-as'/></svg>", UiRibbonItemSize.Small, !EditMode, null, new Func<Task>(() => { RaiseUiActionRequested("SaveAs"); return Task.CompletedTask; })),
            new UiRibbonAction("DeleteFavorite", localizer["Ribbon_DeleteFavorite"].Value, "<svg><use href='/icons/sprite.svg#trash'/></svg>", UiRibbonItemSize.Small, !ActiveFavoriteId.HasValue, null, new Func<Task>(() => { RaiseUiActionRequested("DeleteFavorite"); return Task.CompletedTask; })),
            new UiRibbonAction("Reload", localizer["Ribbon_ReloadData"].Value, "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, false, null, new Func<Task>(() => { RaiseUiActionRequested("Reload"); return Task.CompletedTask; })) { MobileShortcut = true }
        }));

        // Filters
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Filter"], new List<UiRibbonAction>
        {
            new UiRibbonAction("FiltersOpen", localizer["Ribbon_OpenFilters"].Value, "<svg><use href='/icons/sprite.svg#filters'/></svg>", UiRibbonItemSize.Small, FilterOptionsLoading || !EditMode, null, new Func<Task>(() => { RaiseUiActionRequested("FiltersOpen"); return Task.CompletedTask; })),
            new UiRibbonAction("FiltersClear", localizer["Ribbon_ClearFilters"].Value, "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, GetSelectedFiltersCount()==0, null, new Func<Task>(() => { RaiseUiActionRequested("FiltersClear"); return Task.CompletedTask; }))
        }));

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }

    /// <summary>
    /// Opens the favorite dialog and prepares state for creating or updating a favorite.
    /// </summary>
    /// <param name="update">True when updating an existing favorite; false when creating a new one.</param>
    /// <param name="resetNameIfNew">Optional default name used when creating a new favorite.</param>
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

    /// <summary>
    /// Closes the favorite dialog without performing any save/update operations.
    /// </summary>
    public void CloseFavoriteDialog()
    {
        ShowFavoriteDialog = false;
        RaiseStateChanged();
    }

    /// <summary>
    /// Submits the favorite dialog: creates or updates a favorite depending on dialog mode.
    /// </summary>
    /// <param name="defaultName">Default name to use if the user provided no name.</param>
    /// <param name="primaryKind">Primary posting kind.</param>
    /// <param name="includeCategory">Whether category grouping is included.</param>
    /// <param name="interval">Interval value.</param>
    /// <param name="take">Number of periods to take.</param>
    /// <param name="comparePrevious">Whether previous comparison is enabled.</param>
    /// <param name="compareYear">Whether year comparison is enabled.</param>
    /// <param name="compareProjection">Whether projected-period comparison is enabled.</param>
    /// <param name="showChart">Whether chart is shown for this favorite.</param>
    /// <param name="expandable">Whether table rows are expandable.</param>
    /// <param name="postingKinds">Optional posting kinds when multi-kind selection is used.</param>
    /// <param name="filters">Optional filters payload.</param>
    /// <param name="useValutaDate">Whether to use Valuta date for this favorite.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated <see cref="ReportFavoriteDto"/>, or <c>null</c> when the operation failed.</returns>
    public async Task<ReportFavoriteDto?> SubmitFavoriteDialogAsync(string defaultName, PostingKind primaryKind, bool includeCategory, int interval, int take, bool comparePrevious, bool compareYear, bool compareProjection, bool showChart, bool expandable, IReadOnlyCollection<PostingKind>? postingKinds, ReportAggregatesFiltersRequest? filters, bool useValutaDate = false, CancellationToken ct = default)
    {
        FavoriteError = null;
        try
        {
            if (FavoriteDialogIsUpdate && ActiveFavoriteId.HasValue)
            {
                var res = await UpdateFavoriteAsync(ActiveFavoriteId.Value,
                    FavoriteName.Trim(), primaryKind, includeCategory, interval, take,
                    comparePrevious, compareYear, compareProjection, showChart, expandable, postingKinds, filters, useValutaDate, ct);
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
                    comparePrevious, compareYear, compareProjection, showChart, expandable, postingKinds, filters, useValutaDate, ct);
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
}
