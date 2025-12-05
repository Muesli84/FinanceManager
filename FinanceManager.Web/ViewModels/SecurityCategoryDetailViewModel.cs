using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.ViewModels;

public sealed class SecurityCategoryDetailViewModel : ViewModelBase
{
    private readonly FinanceManager.Shared.IApiClient _api;

    public SecurityCategoryDetailViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<FinanceManager.Shared.IApiClient>();
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
            var dto = await _api.SecurityCategories_GetAsync(Id.Value, ct);
            if (dto is not null)
            {
                Model.Name = dto.Name ?? string.Empty;
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
            }
            else
            {
                Error = "Err_NotFound";
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
            var created = await _api.SecurityCategories_CreateAsync(new SecurityCategoryRequest { Name = Model.Name }, ct);
            if (created is null)
            {
                Error = _api.LastError;
                RaiseStateChanged();
                return false;
            }
            return true;
        }
        else
        {
            var updated = await _api.SecurityCategories_UpdateAsync(Id!.Value, new SecurityCategoryRequest { Name = Model.Name }, ct);
            if (updated is null)
            {
                Error = _api.LastError;
                RaiseStateChanged();
                return false;
            }
            return true;
        }
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id is null) { return false; }
        var ok = await _api.SecurityCategories_DeleteAsync(Id.Value, ct);
        if (!ok)
        {
            Error = _api.LastError;
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
