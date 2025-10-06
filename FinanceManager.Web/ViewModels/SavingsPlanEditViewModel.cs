using System.Net.Http.Json;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels;

public sealed class SavingsPlanEditViewModel : ViewModelBase
{
    private readonly HttpClient _http;

    public SavingsPlanEditViewModel(IServiceProvider sp, IHttpClientFactory httpFactory) : base(sp)
    {
        _http = httpFactory.CreateClient("Api");
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
            var resp = await _http.GetAsync($"/api/savings-plans/{Id}", ct);
            if (resp.IsSuccessStatusCode)
            {
                var dto = await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct);
                if (dto != null)
                {
                    Model.Name = dto.Name;
                    Model.Type = dto.Type;
                    Model.TargetAmount = dto.TargetAmount;
                    Model.TargetDate = dto.TargetDate;
                    Model.Interval = dto.Interval;
                    Model.CategoryId = dto.CategoryId;
                    Model.ContractNumber = dto.ContractNumber;
                    await LoadAnalysisAsync(ct);
                }
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
        var resp = await _http.GetAsync($"/api/savings-plans/{Id}/analysis", ct);
        if (resp.IsSuccessStatusCode)
        {
            Analysis = await resp.Content.ReadFromJsonAsync<SavingsPlanAnalysisDto>(cancellationToken: ct);
        }
        RaiseStateChanged();
    }

    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        var resp = await _http.GetAsync("/api/savings-plan-categories", ct);
        if (resp.IsSuccessStatusCode)
        {
            Categories = await resp.Content.ReadFromJsonAsync<List<SavingsPlanCategoryDto>>(cancellationToken: ct) ?? new();
        }
        RaiseStateChanged();
    }

    public async Task<SavingsPlanDto?> SaveAsync(CancellationToken ct = default)
    {
        Error = null;
        if (IsEdit)
        {
            var resp = await _http.PutAsJsonAsync($"/api/savings-plans/{Id}", Model, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return null;
            }
            var existing = await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct);
            RaiseStateChanged();
            return existing;
        }
        else
        {
            var resp = await _http.PostAsJsonAsync("/api/savings-plans", Model, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Error = await resp.Content.ReadAsStringAsync(ct);
                RaiseStateChanged();
                return null;
            }
            var dto = await resp.Content.ReadFromJsonAsync<SavingsPlanDto>(cancellationToken: ct);
            RaiseStateChanged();
            return dto;
        }
    }

    public async Task<bool> ArchiveAsync(CancellationToken ct = default)
    {
        if (!IsEdit || Id == null) { return false; }
        var resp = await _http.PostAsync($"/api/savings-plans/{Id}/archive", content: null, ct);
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
        if (!IsEdit || Id == null) { return false; }
        var resp = await _http.DeleteAsync($"/api/savings-plans/{Id}", ct);
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
    }
}
