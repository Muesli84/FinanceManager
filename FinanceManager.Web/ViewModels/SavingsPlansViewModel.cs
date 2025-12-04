using Microsoft.Extensions.Localization;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.SavingsPlans;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlansViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private readonly IApiClient _api;

    public SavingsPlansViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
        _api = sp.GetService<IApiClient>() ?? new ApiClient(_http);
    }

    public bool Loaded { get; private set; }
    public bool IsAuthenticated => base.IsAuthenticated;

    public bool ShowActiveOnly { get; private set; } = true;
    public List<SavingsPlanDto> Plans { get; private set; } = new();

    private readonly Dictionary<Guid, SavingsPlanAnalysisDto> _analysisByPlan = new();
    private readonly Dictionary<Guid, Guid?> _displaySymbolByPlan = new();

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var actions = new UiRibbonGroup(localizer["Ribbon_Group_Actions"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["BtnNew"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New"),
            new UiRibbonItem(localizer["Ribbon_Categories"], "<svg><use href='/icons/sprite.svg#groups'/></svg>", UiRibbonItemSize.Small, false, "Categories")
        });
        var filter = new UiRibbonGroup(localizer["Ribbon_Group_Filter"], new List<UiRibbonItem>
        {
            new UiRibbonItem(ShowActiveOnly ? localizer["OnlyActive"] : localizer["StatusArchived"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, false, "ToggleActive")
        });
        return new List<UiRibbonGroup> { actions, filter };
    }

    public void ToggleActiveOnly()
    {
        ShowActiveOnly = !ShowActiveOnly;
        _ = InitializeAsync();
        RaiseStateChanged();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await LoadPlansAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    private async Task LoadPlansAsync(CancellationToken ct)
    {
        Plans.Clear();
        _analysisByPlan.Clear();
        _displaySymbolByPlan.Clear();

        try
        {
            var list = await _api.SavingsPlans_ListAsync(ShowActiveOnly, ct);
            Plans = list.ToList();
        }
        catch
        {
            return;
        }

        // Load category symbols to use as fallback
        var categorySymbolMap = new Dictionary<Guid, Guid?>();
        try
        {
            var clist = await _api.SavingsPlanCategories_ListAsync(ct);
            foreach (var c in clist)
            {
                if (c.Id != Guid.Empty)
                {
                    categorySymbolMap[c.Id] = c.SymbolAttachmentId;
                }
            }
        }
        catch { }

        // Build display symbol mapping per plan (plan symbol -> contact/category fallback)
        foreach (var p in Plans)
        {
            Guid? display = null;
            if (p.SymbolAttachmentId.HasValue)
            {
                display = p.SymbolAttachmentId;
            }
            else if (p.CategoryId.HasValue && categorySymbolMap.TryGetValue(p.CategoryId.Value, out var catSym) && catSym.HasValue)
            {
                display = catSym;
            }
            _displaySymbolByPlan[p.Id] = display;
        }

        await LoadAnalysesAsync(ct);
    }

    private async Task LoadAnalysesAsync(CancellationToken ct)
    {
        _analysisByPlan.Clear();
        if (Plans.Count == 0) { return; }
        var tasks = Plans.Select(async p =>
        {
            try
            {
                var dto = await _api.SavingsPlans_AnalyzeAsync(p.Id, ct);
                if (dto != null) { _analysisByPlan[p.Id] = dto; }
            }
            catch { }
        });
        await Task.WhenAll(tasks);
    }

    public string GetStatusLabel(IStringLocalizer localizer, SavingsPlanDto plan)
    {
        var state = GetState(plan);
        return state switch
        {
            PlanState.Done => localizer["StatusDone"],
            PlanState.Unreachable => localizer["StatusUnreachable"],
            _ => plan.IsActive ? localizer["StatusActive"] : localizer["StatusArchived"],
        };
    }

    public (bool Reachable, bool Unreachable) GetStatusFlags(SavingsPlanDto plan)
    {
        var s = GetState(plan);
        return (s == PlanState.Done, s == PlanState.Unreachable);
    }

    private PlanState GetState(SavingsPlanDto plan)
    {
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a) || a.TargetAmount is null || a.TargetDate is null)
        {
            return PlanState.Normal;
        }
        return a.TargetReachable ? PlanState.Done : PlanState.Unreachable;
    }

    private enum PlanState { Normal, Done, Unreachable }

    // Public helper for UI to get the display symbol attachment id (plan symbol or category fallback)
    public Guid? GetDisplaySymbolAttachmentId(SavingsPlanDto plan)
    {
        if (plan == null) return null;
        return _displaySymbolByPlan.TryGetValue(plan.Id, out var v) ? v : null;
    }

    // Public helpers to expose analysis values for the UI
    public decimal? GetAccumulatedAmount(SavingsPlanDto plan)
    {
        if (plan == null) return null;
        return _analysisByPlan.TryGetValue(plan.Id, out var a) ? a.AccumulatedAmount : (decimal?)null;
    }

    public decimal? GetRemainingAmount(SavingsPlanDto plan)
    {
        if (plan == null) return null;
        if (plan.Type == SavingsPlanType.Open) return null;
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a)) return null;
        if (a.TargetAmount is null) return null;
        return a.TargetAmount.Value - a.AccumulatedAmount;
    }

    // New: determine completed / reachable / unreachable semantics for UI
    public bool IsCompleted(SavingsPlanDto plan)
    {
        if (plan == null) return false;
        if (plan.Type == SavingsPlanType.Open) return false;
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a)) return false;
        if (a.TargetAmount is null) return false;
        // completed when accumulated already reached or exceeded target
        return a.AccumulatedAmount >= a.TargetAmount.Value;
    }

    public bool IsReachableButNotCompleted(SavingsPlanDto plan)
    {
        if (plan == null) return false;
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a)) return false;
        if (a.TargetAmount is null) return false;
        return a.TargetReachable && a.AccumulatedAmount < a.TargetAmount.Value;
    }

    public bool IsUnreachable(SavingsPlanDto plan)
    {
        if (plan == null) return false;
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a)) return false;
        if (a.TargetAmount is null) return false;
        return !a.TargetReachable && a.AccumulatedAmount < a.TargetAmount.Value;
    }

    public bool IsOverdue(SavingsPlanDto plan)
    {
        if (plan == null) return false;
        if (!_analysisByPlan.TryGetValue(plan.Id, out var a)) return false;
        if (plan.TargetDate is null) return false;
        if (plan.TargetDate == DateTime.MinValue) return false;
        if (plan.TargetDate > DateTime.Now) return false;
        var remainingAmount = GetRemainingAmount(plan) ?? 0;
        if (remainingAmount <= 0) return false;
        return true;
    }
}
