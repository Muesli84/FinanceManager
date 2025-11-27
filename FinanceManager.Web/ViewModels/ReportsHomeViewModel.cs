using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class ReportsHomeViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public ReportsHomeViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loading { get; private set; }
    public List<FavoriteItem> Favorites { get; } = new();

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
            var list = await _http.GetFromJsonAsync<List<FavoriteItem>>("/api/report-favorites", ct) ?? new();
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

    public sealed record FavoriteItem(Guid Id, string Name, int PostingKind, bool IncludeCategory, int Interval, bool ComparePrevious, bool CompareYear, bool ShowChart, bool Expandable, DateTime CreatedUtc, DateTime? ModifiedUtc, IReadOnlyCollection<int> PostingKinds);
}
