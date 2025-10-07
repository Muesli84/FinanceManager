using System.Net.Http.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlansViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SavingsPlansViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public bool IsAuthenticated => base.IsAuthenticated;

    public bool ShowActiveOnly { get; private set; } = true;
    public List<SavingsPlanDto> Plans { get; private set; } = new();

    private readonly Dictionary<Guid, SavingsPlanAnalysisDto> _analysisByPlan = new();

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
        return new List<UiRibbonGroup>{ actions, filter };
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

        var resp = await _http.GetAsync($"/api/savings-plans?onlyActive={ShowActiveOnly}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            return;
        }
        Plans = await resp.Content.ReadFromJsonAsync<List<SavingsPlanDto>>(cancellationToken: ct) ?? new();
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
                var r = await _http.GetAsync($"/api/savings-plans/{p.Id}/analysis", ct);
                if (r.IsSuccessStatusCode)
                {
                    var dto = await r.Content.ReadFromJsonAsync<SavingsPlanAnalysisDto>(cancellationToken: ct);
                    if (dto != null) { _analysisByPlan[p.Id] = dto; }
                }
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
}
