using FinanceManager.Shared;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlanEditViewModel : ViewModelBase
{
    private readonly IApiClient _api;

    public SavingsPlanEditViewModel(IServiceProvider sp) : base(sp)
    {
        _api = sp.GetRequiredService<IApiClient>();
    }

    public Guid? Id { get; private set; }
    public bool IsEdit => Id.HasValue;

    public string? Error { get; private set; }
    public bool Loaded { get; private set; }

    public SavingsPlanAnalysisDto? Analysis { get; private set; }
    public List<SavingsPlanCategoryDto> Categories { get; private set; } = new();

    public EditModel Model { get; } = new();

    // Navigation context
    public string? BackNav { get; private set; }
    public Guid? ReturnDraftId { get; private set; }
    public Guid? ReturnEntryId { get; private set; }
    public string? PrefillName { get; private set; }

    public string ChartEndpoint => IsEdit && Id.HasValue ? $"/api/savings-plans/{Id}/aggregates" : string.Empty;

    public async Task InitializeAsync(Guid? id, string? backNav, Guid? draftId, Guid? entryId, string? prefillName, CancellationToken ct = default)
    {
        Id = id;
        BackNav = backNav;
        ReturnDraftId = draftId;
        ReturnEntryId = entryId;
        PrefillName = prefillName;

        Error = null;
        Analysis = null;
        if (IsEdit)
        {
            var dto = await _api.SavingsPlans_GetAsync(Id!.Value, ct);
            if (dto != null)
            {
                Model.Name = dto.Name;
                Model.Type = dto.Type;
                Model.TargetAmount = dto.TargetAmount;
                Model.TargetDate = dto.TargetDate;
                Model.Interval = dto.Interval;
                Model.CategoryId = dto.CategoryId;
                Model.ContractNumber = dto.ContractNumber;
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
                await LoadAnalysisAsync(ct);
            }
            else
            {
                Error = "ErrorNotFound";
            }
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(PrefillName) && string.IsNullOrWhiteSpace(Model.Name))
            {
                Model.Name = PrefillName!;
            }
        }
        await LoadCategoriesAsync(ct);
        Loaded = true;
        RaiseStateChanged();
    }

    public async Task LoadAnalysisAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null) { return; }
        try
        {
            Analysis = await _api.SavingsPlans_AnalyzeAsync(Id.Value, ct);
        }
        catch { }
        RaiseStateChanged();
    }

    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        try
        {
            Categories = (await _api.SavingsPlanCategories_ListAsync(ct)).ToList();
        }
        catch
        {
            Categories = new();
        }
        RaiseStateChanged();
    }

    public async Task<SavingsPlanDto?> SaveAsync(CancellationToken ct = default)
    {
        Error = null;
        if (IsEdit)
        {
            var req = new SavingsPlanCreateRequest(Model.Name, Model.Type, Model.TargetAmount, Model.TargetDate, Model.Interval, Model.CategoryId, Model.ContractNumber);
            var existing = await _api.SavingsPlans_UpdateAsync(Id!.Value, req, ct);
            if (existing == null)
            {
                Error = _api.LastError ?? "Error_Update";
                RaiseStateChanged();
                return null;
            }
            RaiseStateChanged();
            return existing;
        }
        else
        {
            var req = new SavingsPlanCreateRequest(Model.Name, Model.Type, Model.TargetAmount, Model.TargetDate, Model.Interval, Model.CategoryId, Model.ContractNumber);
            try
            {
                var dto = await _api.SavingsPlans_CreateAsync(req, ct);
                RaiseStateChanged();
                return dto;
            }
            catch (Exception ex)
            {
                Error = ex.Message;
                RaiseStateChanged();
                return null;
            }
        }
    }

    public async Task<bool> ArchiveAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null) { return false; }
        var ok = await _api.SavingsPlans_ArchiveAsync(Id.Value, ct);
        if (!ok)
        {
            Error = _api.LastError ?? "Error_Archive";
            RaiseStateChanged();
        }
        return ok;
    }

    public async Task<bool> DeleteAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null) { return false; }
        var ok = await _api.SavingsPlans_DeleteAsync(Id.Value, ct);
        if (!ok)
        {
            Error = _api.LastError ?? "Error_Delete";
            RaiseStateChanged();
        }
        return ok;
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
            new UiRibbonItem(localizer["Ribbon_Archive"], "<svg><use href='/icons/sprite.svg#archive'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Archive"),
            new UiRibbonItem(localizer["Ribbon_Delete"], "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Delete")
        });
        var analysis = new UiRibbonGroup(localizer["Ribbon_Group_Analysis"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Recalculate"], "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Recalculate")
        });
        var related = new UiRibbonGroup(localizer["Ribbon_Group_Related"], new List<UiRibbonItem>
        {
            new UiRibbonItem(localizer["Ribbon_Categories"], "<svg><use href='/icons/sprite.svg#groups'/></svg>", UiRibbonItemSize.Small, false, "Categories"),
            new UiRibbonItem(localizer["Ribbon_Postings"], "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Postings"),
            new UiRibbonItem(localizer["Ribbon_Attachments"], "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, !IsEdit, "Attachments")
        });
        return new List<UiRibbonGroup> { nav, edit, related, analysis };
    }

    public sealed class EditModel
    {
        public string Name { get; set; } = string.Empty;
        public SavingsPlanType Type { get; set; } = SavingsPlanType.OneTime;
        public decimal? TargetAmount { get; set; }
        public DateTime? TargetDate { get; set; }
        public SavingsPlanInterval? Interval { get; set; }
        public Guid? CategoryId { get; set; }
        public string? ContractNumber { get; set; }
        // Optional symbol attachment id for UI binding
        public Guid? SymbolAttachmentId { get; set; }
    }
}
