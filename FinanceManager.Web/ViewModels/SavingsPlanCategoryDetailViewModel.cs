using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using FinanceManager.Shared.Dtos.SavingsPlans; // use shared SavingsPlanCategoryDto
using FinanceManager.Shared;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlanCategoryDetailViewModel : ViewModelBase
{
    private readonly HttpClient _http;
    private readonly IApiClient _api;

    public SavingsPlanCategoryDetailViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
        _api = sp.GetService<IApiClient>() ?? new ApiClient(_http);
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
            var dto = await _api.SavingsPlanCategories_GetAsync(Id.Value, ct);
            if (dto is not null)
            {
                Model.Name = dto.Name ?? string.Empty;
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
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
            var created = await _api.SavingsPlanCategories_CreateAsync(new SavingsPlanCategoryDto { Name = Model.Name }, ct);
            if (created is null)
            {
                Error = _api.LastError ?? "Error_Create";
                RaiseStateChanged();
                return false;
            }
            Id = created.Id;
            Model.SymbolAttachmentId = created.SymbolAttachmentId;
            return true;
        }
        else
        {
            var updated = await _api.SavingsPlanCategories_UpdateAsync(Id!.Value, new SavingsPlanCategoryDto { Id = Id!.Value, Name = Model.Name }, ct);
            if (updated is null)
            {
                Error = _api.LastError ?? "Error_Update";
                RaiseStateChanged();
                return false;
            }
            return true;
        }
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id is null) { return false; }
        var ok = await _api.SavingsPlanCategories_DeleteAsync(Id.Value, ct);
        if (!ok)
        {
            Error = _api.LastError ?? "Error_Delete";
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
}
