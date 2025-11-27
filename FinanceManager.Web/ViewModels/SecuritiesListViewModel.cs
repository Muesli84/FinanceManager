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

    // mapping securityId -> display symbol attachment id (security symbol or category fallback)
    private readonly Dictionary<Guid, Guid?> _displaySymbolBySecurity = new();

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
            _displaySymbolBySecurity.Clear();
            RaiseStateChanged();
            return;
        }
        Items = await resp.Content.ReadFromJsonAsync<List<SecurityDto>>(cancellationToken: ct) ?? new();

        // Load categories to get category symbol fallbacks
        var categorySymbolMap = new Dictionary<Guid, Guid?>();
        try
        {
            var creq = await _http.GetAsync("/api/security-categories", ct);
            if (creq.IsSuccessStatusCode)
            {
                var clist = await creq.Content.ReadFromJsonAsync<List<SecurityCategoryDto>>(cancellationToken: ct) ?? new();
                foreach (var c in clist)
                {
                    if (c.Id != Guid.Empty)
                    {
                        categorySymbolMap[c.Id] = c.SymbolAttachmentId;
                    }
                }
            }
        }
        catch { }

        _displaySymbolBySecurity.Clear();
        foreach (var s in Items)
        {
            Guid? display = null;
            if (s.SymbolAttachmentId.HasValue)
            {
                display = s.SymbolAttachmentId;
            }
            else if (s.CategoryId.HasValue && categorySymbolMap.TryGetValue(s.CategoryId.Value, out var catSym) && catSym.HasValue)
            {
                display = catSym;
            }
            _displaySymbolBySecurity[s.Id] = display;
        }

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

    // Public helper for UI to get display symbol attachment id (security symbol or category fallback)
    public Guid? GetDisplaySymbolAttachmentId(SecurityDto security)
    {
        if (security == null) return null;
        return _displaySymbolBySecurity.TryGetValue(security.Id, out var v) ? v : null;
    }
}
