using System.Net.Http.Json;
using FinanceManager.Application;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SecurityCategoriesViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SecurityCategoriesViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public List<CategoryItem> Categories { get; } = new();

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
        var resp = await _http.GetAsync("/api/security-categories", ct);
        if (!resp.IsSuccessStatusCode)
        {
            Categories.Clear();
            RaiseStateChanged();
            return;
        }
        var list = await resp.Content.ReadFromJsonAsync<List<SecurityCategoryDto>>(cancellationToken: ct) ?? new();
        Categories.Clear();
        Categories.AddRange(list.Select(c => new CategoryItem { Id = c.Id, Name = c.Name }).OrderBy(c => c.Name));
        RaiseStateChanged();
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Actions"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New"),
                new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Small, false, "Back")
            })
        };
    }

    public sealed class CategoryItem { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }
}
