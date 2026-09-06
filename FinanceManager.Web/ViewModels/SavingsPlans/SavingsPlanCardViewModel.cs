using FinanceManager.Shared.Dtos.SavingsPlans;
using Microsoft.Extensions.Localization;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.ViewModels.Common;

namespace FinanceManager.Web.ViewModels.SavingsPlans;

/// <summary>
/// View model for the savings plan card. Responsible for loading, creating, updating and deleting
/// savings plans and exposing card fields, analysis and ribbon actions to the UI.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("savings-plans")]
public sealed class SavingsPlanCardViewModel : BaseCardViewModel<(string Key, string Value)>
{
    /// <summary>
    /// Initializes a new instance of <see cref="SavingsPlanCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services such as the API client and localizer.</param>
    public SavingsPlanCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded savings plan. Guid.Empty indicates a new (unsaved) entity.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Returns true when the view model is configured to edit an existing savings plan (Id != Guid.Empty).
    /// </summary>
    public bool IsEdit => Id != Guid.Empty;

    /// <summary>
    /// Last error code or message set by the view model when operations fail.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// True when the view model has performed at least one load operation.
    /// </summary>
    public bool Loaded { get; private set; }

    /// <summary>
    /// Analysis data for the savings plan, populated by <see cref="LoadAnalysisAsync"/>.
    /// </summary>
    public SavingsPlanAnalysisDto? Analysis { get; private set; }

    /// <summary>
    /// Available categories for the savings plan used to populate lookup fields.
    /// </summary>
    /// <returns>The result.</returns>
    public List<SavingsPlanCategoryDto> Categories { get; private set; } = new();

    /// <summary>
    /// Title shown in the card header; falls back to the base title when no model name is available.
    /// </summary>
    public override string Title => Model?.Name ?? base.Title;

    /// <summary>
    /// Editable model used to create or update a savings plan.
    /// </summary>
    /// <param name="null">The null.</param>
    /// <param name="null">The null.</param>
    /// <param name="null">The null.</param>
    /// <param name="null">The null.</param>
    /// <param name="null">The null.</param>
    /// <returns>The result.</returns>
    public SavingsPlanCreateRequest Model { get; private set; } = new(string.Empty, SavingsPlanType.OneTime, null, null, null, null, null);

    /// <summary>
    /// Optional navigation context returning to a draft id after save.
    /// </summary>
    public Guid? ReturnDraftId { get; private set; }

    /// <summary>
    /// Optional navigation context returning to an entry id after save.
    /// </summary>
    public Guid? ReturnEntryId { get; private set; }

    /// <summary>
    /// Endpoint used by the embedded chart view model when editing an existing savings plan.
    /// </summary>
    public string ChartEndpoint => IsEdit ? $"/api/savings-plans/{Id}/aggregates" : string.Empty;

    // remember last loaded dto to rebuild card with context
    private SavingsPlanDto? _loadedDto;

    /// <summary>
    /// Returns an <see cref="AggregateBarChartViewModel"/> when editing an existing savings plan; otherwise <c>null</c>.
    /// </summary>
    public override AggregateBarChartViewModel? ChartViewModel
    {
        get
        {
            if (!IsEdit) return null;
            var title = Localizer?["Chart_Title_SavingsPlan_Aggregates"] ?? Localizer?["General_Title_SavingsPlans"] ?? "Savings Plan";
            var endpoint = ChartEndpoint;
            return new AggregateBarChartViewModel(ServiceProvider, endpoint, title);
        }
    }

    /// <summary>
    /// Loads the savings plan identified by <paramref name="id"/> and prepares the card record for rendering.
    /// When <paramref name="id"/> is <see cref="Guid.Empty"/> a new model is prepared.
    /// </summary>
    /// <param name="id">Identifier of the savings plan to load or <see cref="Guid.Empty"/> for a new entity.</param>
    /// <returns>A task that completes when loading has finished.</returns>
    /// <exception cref="OperationCanceledException">May be thrown by underlying API calls if a cancellation token is used by callers.</exception>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            Analysis = null;

            if (id == Guid.Empty)
            {
                // If a prefill name was provided via InitializeAsync, apply it to the new model
                var initialName = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill : string.Empty;
                Model = new SavingsPlanCreateRequest(initialName, SavingsPlanType.OneTime, null, null, null, null, null);
                await LoadCategoriesAsync(); // ensure categories available for empty/new card
                _loadedDto = null;
                CardRecord = await BuildCardRecordAsync(null);
                return;
            }

            var dto = await ApiClient.SavingsPlans_GetAsync(id);
            if (dto == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Savings plan not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }
            Id = dto.Id;
            Model = new SavingsPlanCreateRequest(dto.Name, dto.Type, dto.TargetAmount, dto.TargetDate, dto.Interval, dto.CategoryId, dto.ContractNumber);

            // Load categories first so the category name can be resolved when building the card
            await LoadCategoriesAsync();

            _loadedDto = dto;
            try
            {
                Analysis = await ApiClient.SavingsPlans_AnalyzeAsync(id);
            }
            catch
            {
                Analysis = null;
            }

            CardRecord = await BuildCardRecordAsync(dto);
        }
        catch (Exception ex)
        {
            CardRecord = new CardRecord(new List<CardField>());
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Loading = false; Loaded = true; RaiseStateChanged(); }
    }

    /// <summary>
    /// Loads analysis data for the current savings plan when editing.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadAnalysisAsync(CancellationToken ct = default)
    {
        if (!IsEdit) { return; }
        try { Analysis = await ApiClient.SavingsPlans_AnalyzeAsync(Id, ct); } catch { }
        if (_loadedDto != null)
        {
            CardRecord = await BuildCardRecordAsync(_loadedDto);
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Loads available savings plan categories used by lookup fields.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        try { Categories = (await ApiClient.SavingsPlanCategories_ListAsync(ct)).ToList(); } catch { Categories = new(); }
        RaiseStateChanged();
    }

    /// <summary>
    /// Saves the current model by creating or updating the savings plan via the API.
    /// On success the model and card record are updated and a "Saved" UI action is raised.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True when the save succeeded; otherwise false.</returns>
    public async Task<bool> SaveAsync(CancellationToken ct = default)
    {
        SetError(null, null);
        try
        {
            // Apply any pending field edits into the Model so Save uses latest values
            var req = BuildCreateRequestFromPending();

            if (Id == Guid.Empty)
            {
                req.Parent = TryGetParentLinkFromQuery();

                var dto = await ApiClient.SavingsPlans_CreateAsync(req, ct);
                if (dto == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Error_Create");
                    return false;
                }
                Id = dto.Id;
                Model = req; // reflect saved data into Model
                _loadedDto = dto;
                CardRecord = await BuildCardRecordAsync(dto);
                ClearPendingChanges();
                RaiseUiActionRequested("Saved", Id.ToString());
                return true;
            }
            else
            {
                var existing = await ApiClient.SavingsPlans_UpdateAsync(Id, req, ct);
                if (existing == null) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Error_Update"); return false; }
                Model = req; // reflect saved data into Model
                _loadedDto = existing;
                CardRecord = await BuildCardRecordAsync(existing);
                ClearPendingChanges();
                RaiseUiActionRequested("Saved", Id.ToString());
                return true;
            }
        }
        catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
        finally { RaiseStateChanged(); }
    }

    /// <summary>
    /// Builds a <see cref="SavingsPlanCreateRequest"/> by merging the current model with pending field overrides.
    /// </summary>
    /// <returns>The merged <see cref="SavingsPlanCreateRequest"/> instance.</returns>
    private SavingsPlanCreateRequest BuildCreateRequestFromPending()
    {
        var name = Model.Name;
        var type = Model.Type;
        decimal? targetAmount = Model.TargetAmount;
        DateTime? targetDate = Model.TargetDate;
        SavingsPlanInterval? interval = Model.Interval;
        Guid? categoryId = Model.CategoryId;
        string? contractNumber = Model.ContractNumber;

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Name", out var vName) && vName is string sName && !string.IsNullOrWhiteSpace(sName)) name = sName;

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Type", out var vType))
        {
            var s = vType?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (Enum.TryParse<SavingsPlanType>(s, true, out var parsedType))
                {
                    type = parsedType;
                }
                else
                {
                    // try match against localized enum labels
                    try
                    {
                        foreach (var enumName in Enum.GetNames(typeof(SavingsPlanType)))
                        {
                            var key = $"EnumType_SavingsPlanType_{enumName}";
                            var localized = Localizer?[key];
                            var localizedVal = localized != null && !localized.ResourceNotFound ? localized.Value : null;
                            if (!string.IsNullOrWhiteSpace(localizedVal) && string.Equals(localizedVal, s, StringComparison.OrdinalIgnoreCase))
                            {
                                type = Enum.Parse<SavingsPlanType>(enumName);
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_TargetAmount", out var vAmt))
        {
            switch (vAmt)
            {
                case decimal d: targetAmount = d; break;
                case string ss when decimal.TryParse(ss, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var pd): targetAmount = pd; break;
            }
        }

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_TargetDate", out var vDate))
        {
            var s = vDate?.ToString();
            if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var pd)) targetDate = pd;
        }

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Interval", out var vInterval))
        {
            var s = vInterval?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (Enum.TryParse<SavingsPlanInterval>(s, true, out var pi))
                {
                    interval = pi;
                }
                else
                {
                    // try match localized values
                    try
                    {
                        foreach (var enumName in Enum.GetNames(typeof(SavingsPlanInterval)))
                        {
                            var key = $"EnumType_SavingsPlanInterval_{enumName}";
                            var localized = Localizer?[key];
                            var localizedVal = localized != null && !localized.ResourceNotFound ? localized.Value : null;
                            if (!string.IsNullOrWhiteSpace(localizedVal) && string.Equals(localizedVal, s, StringComparison.OrdinalIgnoreCase))
                            {
                                interval = Enum.Parse<SavingsPlanInterval>(enumName);
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Category", out var vCat))
        {
            if (vCat is LookupItem li) categoryId = li.Key;
            else if (vCat is Guid g) categoryId = g;
            else if (Guid.TryParse(vCat?.ToString() ?? string.Empty, out var pg)) categoryId = pg;
        }

        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_ContractNumber", out var vContract))
        {
            contractNumber = vContract?.ToString() ?? string.Empty;
        }

        return new SavingsPlanCreateRequest(name ?? string.Empty, type, targetAmount, targetDate, interval, categoryId, contractNumber);
    }

    /// <summary>
    /// Deletes the current savings plan via the API.
    /// </summary>
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (Id == Guid.Empty) return false;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.SavingsPlans_DeleteAsync(Id);
            if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed"); return false; }
            RaiseUiActionRequested("Deleted");
            return true;
        }
        catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
        finally { Loading = false; RaiseStateChanged(); }
    }

    private async Task<CardRecord> BuildCardRecordAsync(SavingsPlanDto? dto)
    {
        // Determine effective type taking pending changes into account so UI reflect immediate edits
        SavingsPlanType effectiveType = dto?.Type ?? Model.Type;
        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Type", out var pendingType))
        {
            var s = pendingType?.ToString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                if (Enum.TryParse<SavingsPlanType>(s, true, out var parsed))
                {
                    effectiveType = parsed;
                }
                else
                {
                    try
                    {
                        foreach (var enumName in Enum.GetNames(typeof(SavingsPlanType)))
                        {
                            var key = $"EnumType_SavingsPlanType_{enumName}";
                            var localized = Localizer?[key];
                            var localizedVal = localized != null && !localized.ResourceNotFound ? localized.Value : null;
                            if (!string.IsNullOrWhiteSpace(localizedVal) && string.Equals(localizedVal, s, StringComparison.OrdinalIgnoreCase))
                            {
                                effectiveType = Enum.Parse<SavingsPlanType>(enumName);
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        var intervalEditable = effectiveType == SavingsPlanType.Recurring;
        var showTargetDate = effectiveType != SavingsPlanType.Open; // target date only when not open-ended

        var effectiveCategoryId = dto?.CategoryId ?? Model.CategoryId;
        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Category", out var pendingCategory))
        {
            switch (pendingCategory)
            {
                case LookupItem li:
                    effectiveCategoryId = li.Key;
                    break;
                case Guid g:
                    effectiveCategoryId = g;
                    break;
                case string s when Guid.TryParse(s, out var parsed):
                    effectiveCategoryId = parsed;
                    break;
            }
        }

        var categoryName = string.Empty;
        if (effectiveCategoryId.HasValue)
        {
            categoryName = Categories.FirstOrDefault(c => c.Id == effectiveCategoryId.Value)?.Name ?? string.Empty;
        }

        var fields = new List<CardField>
        {
            new CardField("Card_Caption_SavingsPlan_Name", CardFieldKind.Text, text: dto?.Name ?? Model.Name ?? string.Empty, editable: true),
            new CardField(
                "Card_Caption_SavingsPlan_Category",
                CardFieldKind.Text,
                text: categoryName,
                editable: true,
                lookupType: "SavingsPlanCategory",
                lookupField: "Name",
                valueId: effectiveCategoryId,
                allowAdd: true,
                recordCreationNameSuggestion: dto?.Name ?? Model.Name),
            new CardField("Card_Caption_SavingsPlan_Type", CardFieldKind.Text, text: dto?.Type.ToString() ?? Model.Type.ToString(), editable: true, lookupType: "Enum:SavingsPlanType"),
        };

        if (intervalEditable)
        {
            fields.Add(new CardField("Card_Caption_SavingsPlan_Interval", CardFieldKind.Text, text: dto?.Interval?.ToString() ?? string.Empty, editable: true, lookupType: "Enum:SavingsPlanInterval"));
        }

        fields.Add(new CardField("Card_Caption_SavingsPlan_TargetAmount", CardFieldKind.Currency, amount: dto?.TargetAmount, editable: true));

        if (showTargetDate)
        {
            fields.Add(new CardField("Card_Caption_SavingsPlan_TargetDate", CardFieldKind.Date, text: dto?.TargetDate?.ToString("d", System.Globalization.CultureInfo.CurrentCulture), editable: true));
        }

        if (dto != null)
        {
            fields.Add(new CardField("Card_Caption_SavingsPlan_CurrentAmount", CardFieldKind.Currency, amount: dto.CurrentAmount));

            if (dto.RemainingAmount > 0m)
            {
                fields.Add(new CardField("Card_Caption_SavingsPlan_RemainingAmount", CardFieldKind.Currency, amount: dto.RemainingAmount));
            }

            if (dto.Type == SavingsPlanType.OneTime &&
                dto.RemainingAmount > 0m &&
                dto.TargetDate?.Date > DateTime.Today &&
                Analysis?.RequiredMonthly > 0m)
            {
                fields.Add(new CardField("Card_Caption_SavingsPlan_RequiredMonthly", CardFieldKind.Currency, amount: Analysis.RequiredMonthly));
            }
        }

        fields.Add(new CardField("Card_Caption_SavingsPlan_ContractNumber", CardFieldKind.Text, text: dto?.ContractNumber ?? string.Empty, editable: true));
        fields.Add(new CardField("Card_Caption_SavingsPlan_Symbol", CardFieldKind.Symbol, symbolId: dto?.SymbolAttachmentId, editable: dto?.Id != Guid.Empty));

        var record = new CardRecord(fields, dto);
        return ApplyPendingValues(ApplyEnumTranslations(record));
    }

    /// <summary>
    /// Provides lookup values for fields that support lookups (e.g. SavingsPlanCategory).
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="q">The q.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <returns>The result.</returns>
    public override async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(CardField field, string? q, int skip, int take)
    {
        if (string.Equals(field.LookupType, "SavingsPlanCategory", StringComparison.OrdinalIgnoreCase))
        {
            if (Categories.Count == 0)
            {
                await LoadCategoriesAsync();
            }

            return Categories
                .Where(c => string.IsNullOrWhiteSpace(q) || (c.Name?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
                .OrderBy(c => c.Name)
                .Skip(Math.Max(0, skip))
                .Take(Math.Max(0, Math.Min(200, take)))
                .Select(c => new LookupItem(c.Id, c.Name ?? string.Empty))
                .ToList();
        }

        return await base.QueryLookupAsync(field, q, skip, take);
    }

    /// <summary>
    /// Reacts to field value validation changes and triggers a rebuild of the card record when relevant enum-like fields change.
    /// </summary>
    /// <param name="field">Card field being validated.</param>
    /// <param name="newValue">New value being applied.</param>
    public override void ValidateFieldValue(CardField field, object? newValue)
    {
        base.ValidateFieldValue(field, newValue);
        if (field == null)
        {
            return;
        }

        if (string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Name", StringComparison.OrdinalIgnoreCase))
        {
            var name = newValue?.ToString() ?? string.Empty;
            Model = new SavingsPlanCreateRequest(name, Model.Type, Model.TargetAmount, Model.TargetDate, Model.Interval, Model.CategoryId, Model.ContractNumber);
            RaiseStateChanged();
            return;
        }

        if (string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_TargetDate", StringComparison.OrdinalIgnoreCase))
        {
            var s = newValue?.ToString();
            if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out var dt))
            {
                Model = new SavingsPlanCreateRequest(Model.Name, Model.Type, Model.TargetAmount, dt, Model.Interval, Model.CategoryId, Model.ContractNumber);
            }
            RaiseStateChanged();
        }

        if (string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Type", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Interval", StringComparison.OrdinalIgnoreCase))
        {
            _ = RebuildCardRecordAsync();
        }
    }

    /// <summary>
    /// Rebuilds the current <see cref="CardRecord"/> using the last loaded DTO and pending field values.
    /// Used to refresh dependent fields when certain selections change.
    /// </summary>
    private async Task RebuildCardRecordAsync()
    {
        try
        {
            CardRecord = await BuildCardRecordAsync(_loadedDto);
        }
        catch
        {
        }
        RaiseStateChanged();
    }

    /// <summary>
    /// Stores pending lookup field values and updates the in-memory model for fields that impact save enablement.
    /// </summary>
    /// <param name="field">Card field containing the lookup.</param>
    /// <param name="item">Selected lookup item.</param>
    public override void ValidateLookupField(CardField field, LookupItem? item)
    {
        base.ValidateLookupField(field, item);
        if (field == null)
        {
            return;
        }

        if (string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Category", StringComparison.OrdinalIgnoreCase))
        {
            Model = new SavingsPlanCreateRequest(Model.Name, Model.Type, Model.TargetAmount, Model.TargetDate, Model.Interval, item?.Key, Model.ContractNumber);
            RaiseStateChanged();
            return;
        }

        if (string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Type", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.LabelKey, "Card_Caption_SavingsPlan_Interval", StringComparison.OrdinalIgnoreCase))
        {
            _ = RebuildCardRecordAsync();
        }
    }

    // --- Ribbon provider ---
    /// <summary>
    /// Provides ribbon register definitions for the savings plan card including navigation, manage and analysis actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Ribbon register definitions or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var effectiveName = Model.Name;
        if (_pendingFieldValues.TryGetValue("Card_Caption_SavingsPlan_Name", out var pendingName) && pendingName is string s && !string.IsNullOrWhiteSpace(s))
        {
            effectiveName = s;
        }

        var canSave = !string.IsNullOrWhiteSpace(effectiveName) && effectiveName.Trim().Length >= 2 && HasPendingChanges;

        // Navigation group
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction(
                Id: "Back",
                Label: localizer["Ribbon_Back"].Value,
                IconSvg: "<svg><use href='/icons/sprite.svg#back'/></svg>",
                Size: UiRibbonItemSize.Large,
                Disabled: false,
                Tooltip: null,
                Callback: new Func<Task>(() => { RaiseUiActionRequested("Back"); return Task.CompletedTask; })
            )
        });

        // Manage group (Save, Archive, Delete)
        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction(
                Id: "Save",
                Label: localizer["Ribbon_Save"].Value,
                IconSvg: "<svg><use href='/icons/sprite.svg#save'/></svg>",
                Size: UiRibbonItemSize.Large,
                Disabled: !canSave,
                Tooltip: null,
                Callback: new Func<Task>(async () => { await SaveAsync(); })
            ),
            new UiRibbonAction(
                Id: "Archive",
                Label: localizer["Ribbon_Archive"].Value,
                IconSvg: "<svg><use href='/icons/sprite.svg#archive'/></svg>",
                Size: UiRibbonItemSize.Small,
                Disabled: !IsEdit,
                Tooltip: null,
                Callback: new Func<Task>(async () => { await ArchiveAsync(); })
            ),
            new UiRibbonAction(
                Id: "Delete",
                Label: localizer["Ribbon_Delete"].Value,
                IconSvg: "<svg><use href='/icons/sprite.svg#delete'/></svg>",
                Size: UiRibbonItemSize.Small,
                Disabled: !IsEdit,
                Tooltip: null,
                Callback: new Func<Task>(async () => { await DeleteAsync(); })
            )
        });

        // Linked information group
        var linked = new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction("Category", localizer["Ribbon_Category"].Value, "<svg><use href='/icons/sprite.svg#groups'/></svg>", UiRibbonItemSize.Small, Model.CategoryId is null, null, new Func<Task>(() => { var url = $"/card/savings-plans/categories/{Model.CategoryId}"; RaiseUiActionRequested("OpenCategory", url); return Task.CompletedTask; })),
            new UiRibbonAction("OpenPostings", localizer["Ribbon_Postings"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !IsEdit, null, new Func<Task>(() => { var url = $"/list/postings/savings-plan/{Id}"; RaiseUiActionRequested("OpenPostings", url); return Task.CompletedTask; })),
            new UiRibbonAction("OpenAttachments", localizer["Ribbon_Attachments"].Value, "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, !IsEdit, null, new Func<Task>(() => { RequestOpenAttachments(AttachmentEntityKind.SavingsPlan, Id); return Task.CompletedTask; }))
        });

        // Analysis group
        var analysis = new UiRibbonTab(localizer["Ribbon_Group_Analysis"].Value, new List<UiRibbonAction>
        {
            new UiRibbonAction(
                Id: "Recalculate",
                Label: localizer["Ribbon_Recalculate"].Value,
                IconSvg: "<svg><use href='/icons/sprite.svg#refresh'/></svg>",
                Size: UiRibbonItemSize.Small,
                Disabled: !IsEdit,
                Tooltip: null,
                Callback: new Func<Task>(async () => { await LoadAnalysisAsync(); })
            )
        });

        var tabs = new List<UiRibbonTab> { nav, manage, linked, analysis };
        var registers = new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
        var baseRegs = base.GetRibbonRegisterDefinition(localizer);
        if (baseRegs != null) registers.AddRange(baseRegs);
        return registers;
    }

    private async Task<bool> ArchiveAsync()
    {
        if (Id == Guid.Empty) return false;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.SavingsPlans_ArchiveAsync(Id);
            if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Archive failed"); return false; }
            // reload to reflect archived state
            await LoadAsync(Id);
            RaiseUiActionRequested("Archived");
            return true;
        }
        catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Indicates whether symbol uploads are allowed for the current entity.
    /// </summary>
    /// <returns><c>true</c> when an existing savings plan is loaded.</returns>
    protected override bool IsSymbolUploadAllowed() => Id != Guid.Empty;

    /// <summary>
    /// Returns the parent attachment kind and id used for symbol uploads.
    /// </summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Id">Identifier of the entity.</param>
    /// <returns>Tuple with <see cref="AttachmentEntityKind.SavingsPlan"/> and the parent id (or Guid.Empty).</returns>
    protected override (AttachmentEntityKind Kind, Guid ParentId) GetSymbolParent() => (AttachmentEntityKind.SavingsPlan, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Assigns a new symbol attachment to the savings plan by calling the API and reloading the entity.
    /// The method swallows exceptions to keep UI behavior consistent; errors are surfaced elsewhere.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign, or <c>null</c> to clear the symbol.</param>
    /// <returns>A task that completes when the assignment and reload have finished.</returns>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        // Ensure API call to set symbol on savings plan and reload
        try
        {
            if (attachmentId.HasValue)
            {
                await ApiClient.SavingsPlans_SetSymbolAsync(Id, attachmentId.Value);
            }
            else
            {
                await ApiClient.SavingsPlans_ClearSymbolAsync(Id);
            }
            await LoadAsync(Id);
        }
        catch
        {
            // swallow to keep behavior consistent; errors surfaced elsewhere
        }
    }

}
