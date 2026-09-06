using FinanceManager.Shared.Dtos;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace FinanceManager.Web.ViewModels.Securities;

/// <summary>
/// View model for a single security card. Provides load/save/archive/delete operations,
/// symbol attachment support and UI field mapping for the security detail view.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("securities")]
public sealed class SecurityCardViewModel : BaseCardViewModel<(string Key, string Value)>
{
    /// <summary>
    /// Initializes a new instance of <see cref="SecurityCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies required by the view model.</param>
    public SecurityCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded security. <see cref="Guid.Empty"/> when creating a new security.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// DTO representing the currently loaded security or <c>null</c> when none is loaded.
    /// </summary>
    public SecurityDto? Security { get; private set; }

    /// <summary>
    /// Display title for the card, derived from the loaded security name when available.
    /// </summary>
    public override string Title => Security?.Name ?? base.Title;

    // Compatibility with SecurityEdit page
    /// <summary>
    /// Edit model used to gather user inputs for creating or updating a security.
    /// </summary>
    public sealed class EditModel
    {
        /// <summary>Security name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Security identifier (ticker/ISIN as displayed).</summary>
        public string Identifier { get; set; } = string.Empty;
        /// <summary>Optional description text.</summary>
        public string? Description { get; set; }
        /// <summary>Optional AlphaVantage code used for external price lookups.</summary>
        public string? AlphaVantageCode { get; set; }
        /// <summary>Currency ISO code used for this security (e.g. "EUR").</summary>
        public string CurrencyCode { get; set; } = "EUR";
        /// <summary>Optional category id for the security.</summary>
        public Guid? CategoryId { get; set; }
        /// <summary>Optional attachment id for the security symbol.</summary>
        public Guid? SymbolAttachmentId { get; set; }
        /// <summary>Optional region of the security (e.g. "Europa", "Nordamerika") used for regional distribution.</summary>
        public string? Region { get; set; }
        /// <summary>Optional sector of the security (e.g. "Technologie", "Pharma") used for sector distribution.</summary>
        public string? Sector { get; set; }
    }

    /// <summary>
    /// Display model with computed values used for the card header/summary.
    /// </summary>
    public sealed class DisplayModel
    {
        /// <summary>Attachment id of the security used as symbol reference; may be <c>null</c>.</summary>
        public Guid? Id { get; set; }
        /// <summary>Whether the security is active (not archived).</summary>
        public bool IsActive { get; set; }
        /// <summary>Optional category display name.</summary>
        public string? CategoryName { get; set; }
    }

    /// <summary>
    /// Current edit model instance used to collect user input.
    /// </summary>
    /// <returns>The result.</returns>
    public EditModel Model { get; } = new();

    /// <summary>
    /// Display model used for header/status rendering.
    /// </summary>
    /// <returns>The result.</returns>
    public DisplayModel Display { get; private set; } = new();

    /// <summary>
    /// Category list used for the category lookup field.
    /// </summary>
    /// <returns>The result.</returns>
    public List<SecurityCategoryDto> Categories { get; private set; } = new();

    /// <summary>
    /// Last error message encountered by view model operations, or <c>null</c> when none.
    /// </summary>
    public string? Error { get; private set; }

    // Navigation context
    /// <summary>
    /// Optional draft id to return to after save/navigation.
    /// </summary>
    public Guid? ReturnDraftId { get; private set; }

    /// <summary>
    /// Optional entry id to return to after save/navigation.
    /// </summary>
    public Guid? ReturnEntryId { get; private set; }

    /// <summary>
    /// True when this view model is showing an existing security (edit mode); false for create mode.
    /// </summary>
    public bool IsEdit => Id != Guid.Empty;

    /// <summary>
    /// Endpoint used by the aggregate chart component to request time series for the current security.
    /// </summary>
    public string ChartEndpoint => Id != Guid.Empty ? $"/api/securities/{Id}/aggregates" : string.Empty;

    /// <summary>
    /// Chart view model instance for the security aggregates or <c>null</c> when no security is loaded.
    /// </summary>
    public override AggregateBarChartViewModel? ChartViewModel
    {
        get
        {
            if (Id == Guid.Empty) return null;
            var title = Localizer?["Chart_Title_Security"] ?? Localizer?["General_Title_Securities"] ?? "Security";
            return new AggregateBarChartViewModel(ServiceProvider, ChartEndpoint, title);
        }
    }

    /// <summary>
    /// Loads security data for the specified id. When <see cref="Guid.Empty"/> a new security is prepared
    /// and categories are loaded so the UI can create a new entry.
    /// </summary>
    /// <param name="id">Security id to load or <see cref="Guid.Empty"/> to initialize a new security.</param>
    /// <returns>A task that completes once loading has finished.</returns>
    /// <exception cref="Exception">Any unexpected errors are caught and exposed via <see cref="Error"/>; exceptions are not propagated to callers.</exception>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                // New security: apply any prefill provided via BaseCardViewModel.InitPrefill
                Model.Name = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill! : string.Empty;
                Security = new SecurityDto { Id = Guid.Empty, Name = Model.Name };
                // Ensure categories are loaded for lookup field
                await LoadCategoriesAsync();
                CardRecord = await BuildCardRecordAsync(null);
                return;
            }

            var dto = await ApiClient.Securities_GetAsync(id);
            if (dto == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Security not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }
            Id = dto.Id;
            Security = dto;
            CardRecord = await BuildCardRecordAsync(dto);
        }
        catch (Exception ex)
        {
            CardRecord = new CardRecord(new List<CardField>());
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Performs initialization tasks for the card and delegates to base initialization.
    /// </summary>
    /// <param name="id">Security id to initialize for.</param>
    /// <returns>A task that completes when initialization is done.</returns>
    public override async Task InitializeAsync(Guid id)
    {
        await LoadCategoriesAsync();
        await base.InitializeAsync(id);
    }

    /// <summary>
    /// Loads available security categories used by the category lookup.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when categories are loaded; failures fall back to an empty list.</returns>
    public async Task LoadCategoriesAsync(CancellationToken ct = default)
    {
        try { Categories = (await ApiClient.SecurityCategories_ListAsync(ct)).ToList(); } catch { Categories = new(); }
    }

    /// <summary>
    /// Saves the current security by creating or updating it via the API.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created or updated <see cref="SecurityDto"/>, or <c>null</c> when the operation failed.</returns>
    public async Task<SecurityDto?> SaveAsync(CancellationToken ct = default)
    {
        Error = null;
        SetError(null, null);
        try
        {
            // Ensure any pending UI field values are applied to the in-memory Model so request uses latest inputs
            ApplyPendingValuesToModel();
            var req = BuildRequestFromModel();
            if (Id == Guid.Empty)
            {
                req.Parent = TryGetParentLinkFromQuery();

                var dto = await ApiClient.Securities_CreateAsync(req, ct);
                if (dto == null)
                {
                    Error = ApiClient.LastError;
                    SetError(ApiClient.LastErrorCode, ApiClient.LastError);
                    RaiseStateChanged();
                    return null;
                }
                Id = dto.Id;
                Security = dto;
                Display = new DisplayModel { Id = dto.Id, IsActive = dto.IsActive, CategoryName = dto.CategoryName };
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
                CardRecord = await BuildCardRecordAsync(dto);
                RaiseUiActionRequested("Saved", Id.ToString());
                return dto;
            }
            else
            {
                var dto = await ApiClient.Securities_UpdateAsync(Id, req, ct);
                if (dto == null)
                {
                    Error = ApiClient.LastError;
                    SetError(ApiClient.LastErrorCode, ApiClient.LastError);
                    RaiseStateChanged();
                    return null;
                }
                Security = dto;
                Display = new DisplayModel { Id = dto.Id, IsActive = dto.IsActive, CategoryName = dto.CategoryName };
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
                CardRecord = await BuildCardRecordAsync(dto);
                RaiseUiActionRequested("Saved", Id.ToString());
                return dto;
            }
        }
        catch (Exception ex)
        {
            Error = ApiClient.LastError ?? ex.Message;
            SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? ex.Message);
            return null;
        }
        finally
        {
            RaiseStateChanged();
        }
    }

    private SecurityRequest BuildRequestFromModel()
    {
        string GetFieldText(string key, string? fallback = null)
        {
            if (_pendingFieldValues.TryGetValue(key, out var pv))
            {
                if (pv != null)
                {
                    var t = pv.GetType();
                    var nameProp = t.GetProperty("Name");
                    if (nameProp != null)
                    {
                        return nameProp.GetValue(pv)?.ToString() ?? string.Empty;
                    }
                    return pv?.ToString() ?? string.Empty;
                }
            }
            var f = CardRecord?.Fields?.FirstOrDefault(x => x.LabelKey == key);
            if (f != null && !string.IsNullOrWhiteSpace(f.Text)) return f.Text;
            return fallback ?? string.Empty;
        }

        Guid? GetFieldGuidValue(string key, Guid? fallback = null)
        {
            if (_pendingFieldValues.TryGetValue(key, out var pv))
            {
                if (pv != null)
                {
                    var t = pv.GetType();
                    var keyProp = t.GetProperty("Key");
                    if (keyProp != null)
                    {
                        var val = keyProp.GetValue(pv);
                        if (val is Guid gg) return gg;
                        if (val is string s && Guid.TryParse(s, out var parsed)) return parsed;
                    }
                    if (pv is Guid g) return g;
                    if (pv is string s2 && Guid.TryParse(s2, out var parsed2)) return parsed2;
                }
            }
            var f = CardRecord?.Fields?.FirstOrDefault(x => x.LabelKey == key);
            if (f != null && f.ValueId != null && f.ValueId != Guid.Empty) return f.ValueId;
            return fallback;
        }

        var name = GetFieldText("Card_Caption_Security_Name", Model.Name);
        var identifier = GetFieldText("Card_Caption_Security_Identifier", Model.Identifier);
        var alpha = GetFieldText("Card_Caption_Security_AlphaVantage", Model.AlphaVantageCode);
        var currency = GetFieldText("Card_Caption_Security_Currency", Model.CurrencyCode ?? "EUR");
        var description = GetFieldText("Card_Caption_Security_Description", Model.Description);
        var categoryId = GetFieldGuidValue("Card_Caption_Security_Category", Model.CategoryId);
        var region = GetFieldText("Card_Caption_Security_Region", Model.Region);
        var sector = GetFieldText("Card_Caption_Security_Sector", Model.Sector);

        return new SecurityRequest
        {
            Name = name,
            Identifier = identifier,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            AlphaVantageCode = string.IsNullOrWhiteSpace(alpha) ? null : alpha,
            CurrencyCode = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency,
            CategoryId = categoryId,
            Region = string.IsNullOrWhiteSpace(region) ? null : region,
            Sector = string.IsNullOrWhiteSpace(sector) ? null : sector
        };
    }

    private async Task<CardRecord> BuildCardRecordAsync(SecurityDto? dto)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_Security_Name", CardFieldKind.Text, text: dto?.Name ?? Model.Name ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_Identifier", CardFieldKind.Text, text: dto?.Identifier ?? Model.Identifier ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_AlphaVantage", CardFieldKind.Text, text: dto?.AlphaVantageCode ?? Model.AlphaVantageCode ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_Currency", CardFieldKind.Text, text: dto?.CurrencyCode ?? Model.CurrencyCode ?? "EUR", editable: true),
            new CardField("Card_Caption_Security_Category", CardFieldKind.Text, text: dto?.CategoryName ?? (Model.CategoryId.HasValue ? Categories.FirstOrDefault(c => c.Id == Model.CategoryId)?.Name ?? string.Empty : string.Empty), editable: true, lookupType: "SecurityCategory", valueId: dto?.CategoryId ?? Model.CategoryId),
            new CardField("Card_Caption_Security_Description", CardFieldKind.Text, text: dto?.Description ?? Model.Description ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_Region", CardFieldKind.Text, text: dto?.Region ?? Model.Region ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_Sector", CardFieldKind.Text, text: dto?.Sector ?? Model.Sector ?? string.Empty, editable: true),
            new CardField("Card_Caption_Security_Symbol", CardFieldKind.Symbol, symbolId: dto?.SymbolAttachmentId ?? Model.SymbolAttachmentId, editable: dto?.Id != Guid.Empty)
        };

        var record = new CardRecord(fields, dto);
        return ApplyPendingValues(record);
    }

    // --- Ribbon provider ---
    /// <summary>
    /// Builds ribbon register definitions for the security card including navigation, manage and linked actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon register definitions used by the UI.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        // Only allow saving when there are pending changes and required fields (Name + Identifier) are present.
        // CardRecord already reflects pending overrides via ApplyPendingValues.
        var hasRequiredValues = CardRecord != null &&
                                CardRecord.Fields.Any(f => f.LabelKey == "Card_Caption_Security_Name" && !string.IsNullOrWhiteSpace(f.Text)) &&
                                CardRecord.Fields.Any(f => f.LabelKey == "Card_Caption_Security_Identifier" && !string.IsNullOrWhiteSpace(f.Text));
        var canSave = HasPendingChanges && hasRequiredValues;

        var tabs = new List<UiRibbonTab>
        {
            new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, new List<UiRibbonAction>
            {
                new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
            }),

            new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, new List<UiRibbonAction>
            {
                new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !canSave, null, async () => { await SaveAsync(); }) { MobileShortcut = true },
                new UiRibbonAction("Archive", localizer["Ribbon_Archive"].Value, "<svg><use href='/icons/sprite.svg#archive'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty && Security != null && Security.IsActive), null, async () => { await ArchiveAsync(); }),
                new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty && Security != null && !Security.IsActive), null, async () => { await DeleteAsync(); })
            }),

            new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, new List<UiRibbonAction>
            {
                new UiRibbonAction("Postings", localizer["Ribbon_Postings"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty), null, () => { var url = $"/list/postings/security/{Id}"; RaiseUiActionRequested("OpenPostings", url); return Task.CompletedTask; }),
                new UiRibbonAction("Prices", localizer["Ribbon_Prices"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty), null, () => { var url = $"/list/securities/prices/{Id}"; RaiseUiActionRequested("OpenPrices", url); return Task.CompletedTask; }),
                new UiRibbonAction("Performance", localizer["Ribbon_Performance"].Value, "<svg><use href='/icons/sprite.svg#chart'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty), null, () => { var url = $"/securities/{Id}/performance"; RaiseUiActionRequested("OpenPerformance", url); return Task.CompletedTask; }),
                new UiRibbonAction("Attachments", localizer["Ribbon_Attachments"].Value, "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, !(Id != Guid.Empty), null, () => { RequestOpenAttachments(AttachmentEntityKind.Security, Id); return Task.CompletedTask; })
            })
        };

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
    }

    /// <summary>
    /// Archives the currently loaded security via the API and reloads the card to reflect state changes.
    /// </summary>
    /// <returns><c>true</c> when archive succeeded; otherwise <c>false</c>.</returns>
    public async Task<bool> ArchiveAsync()
    {
        if (Id == Guid.Empty) return false;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.Securities_ArchiveAsync(Id);
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
    /// Deletes the currently loaded security via the API.
    /// </summary>
    /// <returns><c>true</c> when deletion succeeded; otherwise <c>false</c>.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (Id == Guid.Empty) return false;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.Securities_DeleteAsync(Id);
            if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed"); return false; }
            RaiseUiActionRequested("Deleted");
            return true;
        }
        catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Provides lookup values for fields that support lookups (e.g. SecurityCategory).
    /// </summary>
    /// <param name="field">Card field requesting lookup values.</param>
    /// <param name="q">Optional search query to filter results.</param>
    /// <param name="skip">Number of items to skip for paging.</param>
    /// <param name="take">Maximum number of items to return (server clamps to safe maximum).</param>
    /// <returns>List of matching <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.LookupItem"/> values.</returns>
    public override async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(CardField field, string? q, int skip, int take)
    {
        if (!string.IsNullOrWhiteSpace(field.LookupType) && string.Equals(field.LookupType, "SecurityCategory", StringComparison.OrdinalIgnoreCase))
        {
            // Ensure categories are loaded
            if (Categories == null || Categories.Count == 0)
            {
                await LoadCategoriesAsync();
            }

            // LoadCategoriesAsync always leaves Categories non-null (success or its catch fallback both
            // assign it), but the compiler cannot see that guarantee across the await above, so fall back
            // to an empty list defensively rather than risk a null dereference.
            var categories = Categories ?? new List<SecurityCategoryDto>();
            var list = categories
                .Where(c => string.IsNullOrWhiteSpace(q) || c.Name.Contains(q ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Name)
                .Skip(Math.Max(0, skip))
                .Take(Math.Max(0, Math.Min(200, take)))
                .Select(c => new LookupItem(c.Id, c.Name))
                .ToList();

            return list;
        }

        return await base.QueryLookupAsync(field, q, skip, take);
    }

    /// <summary>
    /// Returns the parent information used for symbol attachments.
    /// </summary>
    /// <returns>Attachment entity kind and the parent id used when uploading symbols.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.Security, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Whether uploading a symbol is allowed in the current state.
    /// </summary>
    /// <returns><c>true</c> when a symbol upload is allowed for the current security.</returns>
    protected override bool IsSymbolUploadAllowed() => Id != Guid.Empty;

    /// <summary>
    /// Assigns a newly uploaded symbol (attachment) to the security. This method updates the server
    /// and reloads the security record; failures are swallowed to avoid interrupting UI flow.
    /// </summary>
    /// <param name="attachmentId">Attachment id of the uploaded symbol, or <c>null</c> to clear the symbol.</param>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            if (attachmentId.HasValue)
            {
                await ApiClient.Securities_SetSymbolAsync(Id, attachmentId.Value);
            }
            else
            {
                await ApiClient.Securities_ClearSymbolAsync(Id);
            }
            await LoadAsync(Id);
        }
        catch
        {
            // swallow
        }
    }

    // Apply known pending field values into the EditModel so BuildRequestFromModel can rely on Model as source of truth
    private void ApplyPendingValuesToModel()
    {
        try
        {
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Name", out var vName) && vName is string sName)
                Model.Name = sName;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Identifier", out var vIdent) && vIdent is string sIdent)
                Model.Identifier = sIdent;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_AlphaVantage", out var vAlpha) && vAlpha is string sAlpha)
                Model.AlphaVantageCode = sAlpha;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Currency", out var vCurr) && vCurr is string sCurr)
                Model.CurrencyCode = sCurr;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Description", out var vDesc) && vDesc is string sDesc)
                Model.Description = sDesc;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Region", out var vRegion) && vRegion is string sRegion)
                Model.Region = sRegion;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Sector", out var vSector) && vSector is string sSector)
                Model.Sector = sSector;
            if (_pendingFieldValues.TryGetValue("Card_Caption_Security_Category", out var vCat))
            {
                switch (vCat)
                {
                    case FinanceManager.Web.ViewModels.Common.BaseViewModel.LookupItem li:
                        Model.CategoryId = li.Key;
                        break;
                    case Guid g:
                        Model.CategoryId = g;
                        break;
                    case string s when Guid.TryParse(s, out var pg):
                        Model.CategoryId = pg; break;
                }
            }
        }
        catch { /* don't fail saving due to model mapping issues */ }
    }
}
