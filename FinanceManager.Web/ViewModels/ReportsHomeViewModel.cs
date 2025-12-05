using FinanceManager.Shared;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class ReportsHomeViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public ReportsHomeViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
    }

    public bool Loading { get; private set; }
    public List<ReportFavoriteDto> Favorites { get; } = new();

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await ReloadAsync(ct);
        RaiseStateChanged();
    }

    public async Task ReloadAsync(CancellationToken ct = default)
    {
        if (Loading) { return; }
        Loading = true; RaiseStateChanged();
        try
        {
            var list = await _api.Reports_ListFavoritesAsync(ct);
            Favorites.Clear();
            Favorites.AddRange(list.OrderBy(f => f.Name));
        }
        catch { }
        finally { Loading = false; RaiseStateChanged(); }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var actions = new UiRibbonGroup(localizer["Ribbon_Group_Actions"], new()
        {
            new UiRibbonItem(localizer["Ribbon_Reload"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, Loading, "Reload"),
            new UiRibbonItem(localizer["Ribbon_NewReport"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "NewReport")
        });
        return new List<UiRibbonGroup> { actions };
    }
}
