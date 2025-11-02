using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class ContactCategoriesViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public ContactCategoriesViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; private set; }

    [Required, MinLength(2)]
    public string CreateName { get => _createName; set { if (_createName != value) { _createName = value; RaiseStateChanged(); } } }
    private string _createName = string.Empty;

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
        try
        {
            var resp = await _http.GetAsync("/api/contact-categories", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<ContactCategoryDto>>(cancellationToken: ct) ?? new();
                Categories.Clear();
                Categories.AddRange(list.Select(l => new CategoryItem { Id = l.Id, Name = l.Name, SymbolAttachmentId = l.SymbolAttachmentId }).OrderBy(c => c.Name));
                RaiseStateChanged();
            }
        }
        catch { }
    }

    public async Task CreateAsync(CancellationToken ct = default)
    {
        if (Busy) { return; }
        Busy = true; Error = null; RaiseStateChanged();
        // Ensure caller can observe Busy=true even if HTTP completes synchronously
        await Task.Yield();
        try
        {
            var resp = await _http.PostAsJsonAsync("/api/contact-categories", new { Name = CreateName }, ct);
            if (resp.IsSuccessStatusCode)
            {
                CreateName = string.Empty;
                await LoadAsync(ct);
            }
            else
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Busy = false; RaiseStateChanged();
        }
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
            }),
            new UiRibbonGroup(localizer["Ribbon_Group_Actions"], new List<UiRibbonItem>
            {
                new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New")
            })
        };
    }

    public sealed class CategoryItem { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public Guid? SymbolAttachmentId { get; set; } }
    public sealed record ContactCategoryDto(Guid Id, string Name, Guid? SymbolAttachmentId);
}
