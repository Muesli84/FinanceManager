using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SecurityEditViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SecurityEditViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public Guid? Id { get; private set; }
    public bool IsEdit => Id.HasValue;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }

    public string? BackNav { get; private set; }
    public Guid? ReturnDraftId { get; private set; }
    public Guid? ReturnEntryId { get; private set; }
    public string? PrefillName { get; private set; }

    public EditModel Model { get; } = new();
    public DisplayModel Display { get; private set; } = new();
    public List<SecurityCategoryDto> Categories { get; private set; } = new();

    public async Task InitializeAsync(Guid? id, string? backNav, Guid? draftId, Guid? entryId, string? prefillName, CancellationToken ct = default)
    {
        Id = id; BackNav = backNav; ReturnDraftId = draftId; ReturnEntryId = entryId; PrefillName = prefillName;
        Error = null; Loaded = false; Display = new DisplayModel();
        await LoadCategoriesAsync(ct);
        if (IsEdit)
        {
            var resp = await _http.GetAsync($"/api/securities/{Id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct);
                if (dto != null)
                {
                    Display = new DisplayModel { Id = dto.Id, IsActive = dto.IsActive, CategoryName = dto.CategoryName };
                    Model.Name = dto.Name;
                    Model.Identifier = dto.Identifier;
                    Model.Description = dto.Description;
                    Model.AlphaVantageCode = dto.AlphaVantageCode;
                    Model.CurrencyCode = dto.CurrencyCode;
                    Model.CategoryId = dto.CategoryId;
                    Loaded = true;
                }
            }
            else
            {
                Error = "ErrorNotFound";
            }
        }
        else
        {
            Display = new DisplayModel { IsActive = true };
            if (!string.IsNullOrWhiteSpace(PrefillName) && string.IsNullOrWhiteSpace(Model.Name))
            {
                Model.Name = PrefillName!;
            }
            Loaded = true;
        }
        RaiseStateChanged();
    }

    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/security-categories", ct);
        if (resp.IsSuccessStatusCode)
        {
            Categories = await resp.Content.ReadFromJsonAsync<List<SecurityCategoryDto>>(cancellationToken: ct) ?? new();
        }
    }

    public async Task<SecurityDto?> SaveAsync(CancellationToken ct = default)
    {
        Error = null;
        if (IsEdit)
        {
            var resp = await _http.PutAsJsonAsync($"/api/securities/{Id}", Model, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return null;
            }
            var dto = await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct);
            RaiseStateChanged();
            return dto;
        }
        else
        {
            var resp = await _http.PostAsJsonAsync("/api/securities", Model, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return null;
            }
            var dto = await resp.Content.ReadFromJsonAsync<SecurityDto>(cancellationToken: ct);
            RaiseStateChanged();
            return dto;
        }
    }

    public async Task<bool> ArchiveAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null || !Display.IsActive) { return false; }
        var resp = await _http.PostAsync($"/api/securities/{Id}/archive", content: null, ct);
        if (!resp.IsSuccessStatusCode)
        {
            Error = await resp.Content.ReadAsStringAsync(ct);
            RaiseStateChanged();
            return false;
        }
        return true;
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null || Display.IsActive) { return false; }
        var resp = await _http.DeleteAsync($"/api/securities/{Id}", ct);
        if (!resp.IsSuccessStatusCode)
        {
            Error = await resp.Content.ReadAsStringAsync(ct);
            RaiseStateChanged();
            return false;
        }
        return true;
    }

    public override IReadOnlyList<UiRibbonGroup> GetRibbon(IStringLocalizer localizer)
    {
        var nav = new UiRibbonGroup(localizer["Ribbon_Group_Navigation"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Back"], "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, "Back")
        });
        var canSave = !string.IsNullOrWhiteSpace(Model.Name) && Model.Name.Trim().Length >= 2 && !string.IsNullOrWhiteSpace(Model.Identifier) && Model.Identifier.Trim().Length >= 3;
        var edit = new UiRibbonGroup(localizer["Ribbon_Group_Edit"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Save"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !canSave, "Save"),
            new UiRibbonItem(localizer["Ribbon_Archive"], "<svg><use href='/icons/sprite.svg#archive'/></svg>", UiRibbonItemSize.Small, !(IsEdit && Loaded && Display.IsActive), "Archive"),
            new UiRibbonItem(localizer["Ribbon_Delete"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, !(IsEdit && Loaded && !Display.IsActive), "Delete")
        });
        var related = new UiRibbonGroup(localizer["Ribbon_Group_Related"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Postings"], "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !IsEdit || !Loaded, "Postings"),
            new UiRibbonItem(localizer["Ribbon_Prices"], "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !IsEdit || !Loaded, "Prices"),
            new UiRibbonItem(localizer["Ribbon_Attachments"], "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, !IsEdit || !Loaded, "Attachments")
        });
        return new List<UiRibbonGroup> { nav, edit, related };
    }

    public sealed class EditModel
    {
        [Required, MinLength(2)] public string Name { get; set; } = string.Empty;
        [Required, MinLength(3)] public string Identifier { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? AlphaVantageCode { get; set; }
        public string CurrencyCode { get; set; } = "EUR";
        public Guid? CategoryId { get; set; }
    }
    public sealed class DisplayModel
    {
        public Guid? Id { get; set; }
        public bool IsActive { get; set; }
        public string? CategoryName { get; set; }
    }
}
