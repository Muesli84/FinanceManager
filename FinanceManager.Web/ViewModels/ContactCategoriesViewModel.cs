using FinanceManager.Shared; // IApiClient
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.ViewModels;

public sealed class ContactCategoriesViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public ContactCategoriesViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
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
            var list = await _api.ContactCategories_ListAsync(ct);
            Categories.Clear();
            Categories.AddRange(list.Select(l => new CategoryItem { Id = l.Id, Name = l.Name, SymbolAttachmentId = l.SymbolAttachmentId }).OrderBy(c => c.Name));
            RaiseStateChanged();
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
            var created = await _api.ContactCategories_CreateAsync(new ContactCategoryCreateRequest(CreateName), ct);
            if (created is not null)
            {
                CreateName = string.Empty;
                await LoadAsync(ct);
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
}
