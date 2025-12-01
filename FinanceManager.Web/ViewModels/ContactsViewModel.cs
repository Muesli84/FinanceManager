using Microsoft.Extensions.Localization;
using FinanceManager.Shared.Dtos.Contacts; // ContactDto, ContactCategoryDto

namespace FinanceManager.Web.ViewModels;

public sealed class ContactsViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public ContactsViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public bool Loaded { get; private set; }
    public bool IsLoading { get; private set; }
    public bool AllLoaded { get; private set; }

    public string Filter { get; private set; } = string.Empty;

    public List<ContactItem> Contacts { get; } = new();

    private readonly Dictionary<Guid, string> _categoryNames = new();
    private readonly Dictionary<Guid, Guid?> _categorySymbols = new();

    public override async ValueTask InitializeAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated)
        {
            RequireAuthentication(null);
            return;
        }
        await LoadCategoriesAsync(ct);
        await LoadMoreAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    private async Task LoadCategoriesAsync(CancellationToken ct)
    {
        try
        {
            var resp = await _http.GetAsync("/api/contact-categories", ct);
            if (resp.IsSuccessStatusCode)
            {
                var list = await resp.Content.ReadFromJsonAsync<List<ContactCategoryDto>>(cancellationToken: ct) ?? new();
                _categoryNames.Clear();
                _categorySymbols.Clear();
                foreach (var c in list)
                {
                    _categoryNames[c.Id] = c.Name;
                    _categorySymbols[c.Id] = c.SymbolAttachmentId;
                }
            }
        }
        catch { }
    }

    public async Task LoadMoreAsync(CancellationToken ct = default)
    {
        if (!IsAuthenticated || IsLoading || AllLoaded) { return; }
        IsLoading = true;
        RaiseStateChanged();
        try
        {
            var pageSize = 50;
            var url = $"/api/contacts?skip={Contacts.Count}&take={pageSize}";
            if (!string.IsNullOrWhiteSpace(Filter))
            {
                url += $"&q={Uri.EscapeDataString(Filter)}";
            }
            var resp = await _http.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
            {
                var more = await resp.Content.ReadFromJsonAsync<List<ContactDto>>(cancellationToken: ct) ?? new();
                if (more.Count < pageSize)
                {
                    AllLoaded = true;
                }
                foreach (var dto in more)
                {
                    Guid? categoryId = dto.CategoryId;
                    Contacts.Add(new ContactItem
                    {
                        Id = dto.Id,
                        Name = dto.Name,
                        Type = dto.Type.ToString(),
                        CategoryName = categoryId.HasValue && _categoryNames.TryGetValue(categoryId.Value, out var cat)
                            ? cat
                            : string.Empty,
                        SymbolAttachmentId = dto.SymbolAttachmentId,
                        CategorySymbolAttachmentId = categoryId.HasValue && _categorySymbols.TryGetValue(categoryId.Value, out var cs)
                            ? cs
                            : null
                    });
                }
            }
        }
        catch { }
        finally
        {
            IsLoading = false;
            RaiseStateChanged();
        }
    }

    public async Task ResetAndReloadAsync(CancellationToken ct = default)
    {
        AllLoaded = false;
        Contacts.Clear();
        await LoadMoreAsync(ct);
    }

    public async Task SetFilterAsync(string filter, CancellationToken ct = default)
    {
        Filter = filter ?? string.Empty;
        await ResetAndReloadAsync(ct);
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var items = new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_New"], "<svg><use href='/icons/sprite.svg#plus'/></svg>", UiRibbonItemSize.Large, false, "New"),
            new UiRibbonItem(localizer["Ribbon_Reload"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, false, "Reload")
        };
        if (!string.IsNullOrWhiteSpace(Filter))
        {
            items.Add(new UiRibbonItem(localizer["Ribbon_ClearFilter"], "<svg><use href='/icons/sprite.svg#clear'/></svg>", UiRibbonItemSize.Small, false, "ClearFilter"));
        }
        // Add link to contact categories management
        items.Add(new UiRibbonItem(localizer["Ribbon_Categories"], "<svg><use href='/icons/sprite.svg#groups'/></svg>", UiRibbonItemSize.Small, false, "Categories"));
        return new List<UiRibbonGroup>
        {
            new UiRibbonGroup(localizer["Ribbon_Group_Actions"], items)
        };
    }

    public sealed class ContactItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public Guid? SymbolAttachmentId { get; set; }
        public Guid? CategorySymbolAttachmentId { get; set; }
        public Guid? DisplaySymbolAttachmentId => SymbolAttachmentId ?? CategorySymbolAttachmentId;
    }
}
