using System.Net.Http.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SecuritiesListViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SecuritiesListViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public List<SecurityDto> Items { get; private set; } = new();
    public bool OnlyActive { get; private set; } = true;

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated) { return; }
        var resp = await _http.GetAsync($"/api/securities?onlyActive={OnlyActive}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            Items = new();
            RaiseStateChanged();
            return;
        }
        Items = await resp.Content.ReadFromJsonAsync<List<SecurityDto>>(cancellationToken: ct) ?? new();
        RaiseStateChanged();
    }

    public void ToggleActive()
    {
        OnlyActive = !OnlyActive;
        _ = InitializeAsync();
        RaiseStateChanged();
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var actions = new UiRibbonGroup(localizer["Ribbon_Group_Actions"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New"),
            new UiRibbonItem(localizer["Ribbon_Categories"], "<svg><use href='/icons/sprite.svg#groups'/></svg>", UiRibbonItemSize.Small, false, "Categories")
        });
        var filter = new UiRibbonGroup(localizer["Ribbon_Group_Filter"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_ToggleActive"], "<svg><use href='/icons/sprite.svg#check'/></svg>", UiRibbonItemSize.Small, false, "ToggleActive")
        });
        return new List<UiRibbonGroup> { actions, filter };
    }
}
