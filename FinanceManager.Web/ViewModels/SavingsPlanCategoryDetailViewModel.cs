using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlanCategoryDetailViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SavingsPlanCategoryDetailViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
    }

    public Guid? Id { get; private set; }
    public bool IsEdit => Id.HasValue;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }

    public EditModel Model { get; } = new();

    public async Task InitializeAsync(Guid? id, CancellationToken ct = default)
    {
        Id = id;
        Error = null;
        if (IsEdit)
        {
            var resp = await _http.GetAsync($"/api/savings-plan-categories/{Id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<SavingsPlanCategoryDto>(cancellationToken: ct);
                if (dto is not null)
                {
                    Model.Name = dto.Name ?? string.Empty;
                    Model.SymbolAttachmentId = dto.SymbolAttachmentId;
                }
            }
            else
            {
                Error = "Error_NotFound";
            }
        }
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        Error = null;
        if (!IsEdit)
        {
            var resp = await _http.PostAsJsonAsync("/api/savings-plan-categories", new SavingsPlanCategoryDto { Name = Model.Name }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return false;
            }
            return true;
        }
        else
        {
            var resp = await _http.PutAsJsonAsync($"/api/savings-plan-categories/{Id}", new SavingsPlanCategoryDto { Id = Id!.Value, Name = Model.Name }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return false;
            }
            return true;
        }
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id is null) { return false; }
        var resp = await _http.DeleteAsync($"/api/savings-plan-categories/{Id}", ct);
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
        var canSave = !string.IsNullOrWhiteSpace(Model.Name) && Model.Name.Trim().Length >= 2;
        var edit = new UiRibbonGroup(localizer["Ribbon_Group_Edit"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Save"], "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !canSave, "Save"),
            new UiRibbonItem(localizer["Ribbon_Delete"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Delete"),
        });
        return new List<UiRibbonGroup> { nav, edit };
    }

    public sealed class EditModel
    {
        [Required, MinLength(2)]
        public string Name { get; set; } = string.Empty;
        public Guid? SymbolAttachmentId { get; set; }
    }

    public sealed class SavingsPlanCategoryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public Guid? SymbolAttachmentId { get; set; }
    }
}
