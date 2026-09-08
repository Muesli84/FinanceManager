using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Contacts;
using FinanceManager.Web.Components.Shared;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;
using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.ViewModels.Contacts;


/// <summary>
/// View model for the contact detail card. Responsible for loading, creating, updating and deleting a single contact
/// and exposing card fields and lookup behaviors used by the UI.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("contacts")]
public sealed class ContactCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Initializes a new instance of <see cref="ContactCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve services such as <see cref="IApiClient"/> and localizer.</param>
    public ContactCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded contact.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The loaded contact DTO or <c>null</c> when no contact is loaded.
    /// </summary>
    public ContactDto? Contact { get; private set; }

    /// <summary>
    /// Title displayed for the card. Falls back to base title when no contact is loaded.
    /// </summary>
    public override string Title => Contact?.Name ?? base.Title;

    // Navigation context for create flow
    /// <summary>
    /// Optional back navigation URL supplied by the caller when opening the card.
    /// </summary>
    public string? BackNav { get; private set; }

    /// <summary>
    /// Optional originating draft id used when navigating back to the draft after creating a contact.
    /// </summary>
    public Guid? ReturnDraftId { get; private set; }

    /// <summary>
    /// Optional originating draft entry id used when navigating back to the originating entry.
    /// </summary>
    public Guid? ReturnEntryId { get; private set; }

    /// <summary>
    /// Relative endpoint used by the optional chart view model to fetch aggregates for this contact.
    /// </summary>
    public string ChartEndpoint => Id != Guid.Empty ? $"/api/contacts/{Id}/aggregates" : string.Empty;

    /// <summary>
    /// Optional chart view model instance used to display aggregated data for the contact. Returns <c>null</c> for new contacts.
    /// </summary>
    public override AggregateBarChartViewModel? ChartViewModel
    {
        get
        {
            if (Id == Guid.Empty) return null;
            var title = Localizer?["Chart_Title_Contact_Aggregates"] ?? Localizer?["General_Title_Contacts"] ?? "Contact";
            return new AggregateBarChartViewModel(ServiceProvider, ChartEndpoint, title);
        }
    }

    /// <summary>
    /// Loads the contact for the specified identifier. When <paramref name="id"/> is <see cref="Guid.Empty"/>
    /// a new blank DTO is prepared for creation and any optional prefill value is applied.
    /// </summary>
    /// <param name="id">Identifier of the contact to load, or <see cref="Guid.Empty"/> to initialize a new contact.</param>
    /// <returns>A task that completes when loading is finished. ViewModel state (Loading, CardRecord, LastError) is updated accordingly.</returns>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                var name = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill : string.Empty;
                Contact = new ContactDto(Guid.Empty, name, ContactType.Organization, null, null, false, null);
                CardRecord = await BuildCardRecordAsync(Contact);
                RaiseUiActionRequested("ClearEmbeddedPanel", EmbeddedPanelPosition.AfterCard);
                return;
            }

            Contact = await ApiClient.Contacts_GetAsync(id);
            if (Contact == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Contact not found");
                CardRecord = new CardRecord(new List<CardField>());
                RaiseUiActionRequested("ClearEmbeddedPanel", EmbeddedPanelPosition.AfterCard);
                return;
            }
            CardRecord = await BuildCardRecordAsync(Contact);
            RaiseUiActionRequested("ClearEmbeddedPanel", EmbeddedPanelPosition.AfterCard);
            var panelParameters = new Dictionary<string, object?>
            {
                ["ContactId"] = Contact.Id
            };
            RaiseUiEmbeddedPanelRequested(new BaseViewModel.EmbeddedPanelSpec(typeof(FinanceManager.Web.Components.Pages.ContactDetail), panelParameters, EmbeddedPanelPosition.AfterCard, true));
        }
        catch (Exception ex)
        {
            CardRecord = new CardRecord(new List<CardField>());
            RaiseUiActionRequested("ClearEmbeddedPanel", EmbeddedPanelPosition.AfterCard);
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Convenience helper for requesting the Contact Merge overlay. The overlay spec is raised as a UI action payload.
    /// </summary>
    /// <param name="sourceId">Identifier of the source contact to merge into another.</param>
    /// <param name="sourceType">Type name of the source contact (used to choose merge behavior).</param>
    private void RequestOpenMerge(Guid sourceId, string sourceType)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["Visible"] = true,
            ["SourceId"] = sourceId,
            ["SourceType"] = sourceType,
            // Provide explicit overlay title so pages render correct header for dynamic overlay
            ["OverlayTitle"] = Localizer?["Merge_Title"].Value ?? "Merge"
        };
        var spec = new UiOverlaySpec(typeof(ContactMergePanel), parameters);
        RaiseUiActionRequested("OpenMerge", spec);
    }

    private async Task<CardRecord> BuildCardRecordAsync(ContactDto c)
    {
        var categoryName = string.Empty;
        if (c.CategoryId.HasValue && c.CategoryId.Value != Guid.Empty)
        {
            try
            {
                var cats = await ApiClient.ContactCategories_ListAsync();
                var cat = cats?.FirstOrDefault(x => x.Id == c.CategoryId.Value);
                if (cat != null) categoryName = cat.Name ?? string.Empty;
            }
            catch { }
        }

        var fields = new List<CardField>
        {
            new CardField("Card_Caption_Contact_Name", CardFieldKind.Text, text: c.Name ?? string.Empty, editable: true),
            new CardField("Card_Caption_Contact_Type", CardFieldKind.Text, text: c.Type.ToString(), editable: true, lookupType: "Enum:ContactType"),
            new CardField("Card_Caption_Contact_Category", CardFieldKind.Text, text: categoryName, editable: true, lookupType: "ContactCategory", valueId: c.CategoryId),
            new CardField("Card_Caption_Contact_Description", CardFieldKind.Text, text: c.Description ?? string.Empty, editable: true),
            new CardField("Card_Caption_Contact_Symbol", CardFieldKind.Symbol, symbolId: c.SymbolAttachmentId, editable: c.Id != Guid.Empty),
            new CardField("Card_Caption_Contact_IsPaymentIntermediary", CardFieldKind.Text, text: (c.IsPaymentIntermediary ? BooleanSelection.True:BooleanSelection.False).ToString(), editable: true, lookupType: "Enum:BooleanSelection")
        };

        var record = new CardRecord(fields, c);
        return ApplyPendingValues(ApplyEnumTranslations(record));
    }

    /// <summary>
    /// Performs lookup queries for contact-related lookup types (e.g. contact categories). Falls back to base behavior for other types.
    /// </summary>
    /// <param name="field">Card field describing the lookup configuration.</param>
    /// <param name="q">Search term used to filter results.</param>
    /// <param name="skip">Number of items to skip for paging.</param>
    /// <param name="take">Number of items to take for paging.</param>
    /// <returns>A task that resolves to a list of lookup items matching the query.</returns>
    public override async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(CardField field, string? q, int skip, int take)
    {
        try
        {
            if (string.Equals(field.LookupType, "ContactCategory", StringComparison.OrdinalIgnoreCase))
            {
                var list = await ApiClient.ContactCategories_ListAsync();
                if (!string.IsNullOrWhiteSpace(q))
                {
                    var term = q.Trim();
                    return list.Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                               .Select(c => new LookupItem(c.Id, c.Name)).ToList();
                }
                return list.Select(c => new LookupItem(c.Id, c.Name)).ToList();
            }
        }
        catch { }
        return await base.QueryLookupAsync(field, q, skip, take);
    }

    /// <summary>
    /// Validates and stores lookup field selection. For enum-based contact type selection the text value is updated
    /// and persisted as a pending value; otherwise base behavior is invoked.
    /// </summary>
    /// <param name="field">Field being validated.</param>
    /// <param name="item">Selected lookup item or <c>null</c> to clear.</param>
    public override void ValidateLookupField(CardField field, LookupItem? item)
    {
        if (string.Equals(field.LookupType, "Enum:ContactType", StringComparison.OrdinalIgnoreCase))
        {
            field.Text = item?.Name ?? field.Text;
            ValidateFieldValue(field, field.Text);
            return;
        }
        base.ValidateLookupField(field, item);
    }

    /// <summary>
    /// Reloads the current card by reinitializing with the current Id.
    /// </summary>
    public override async Task ReloadAsync()
    {
        await InitializeAsync(Id);
    }

    /// <summary>
    /// Deletes the current contact.
    /// </summary>
    /// <returns>A task that resolves to <c>true</c> when deletion succeeded; otherwise <c>false</c>.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (Contact == null) return false;
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return false;
        }

        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.Contacts_DeleteAsync(Id);
            if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed"); return false; }
            RaiseUiActionRequested("Deleted");
            return true;
        }
        catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Returns ribbon register definitions used by the card UI including navigation, manage and linked groups.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels for ribbon actions.</param>
    /// <returns>A list of ribbon register definitions to render in the UI.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !HasPendingChanges, null, async () => {
                var dto = BuildDto(CardRecord);
                if (Id == Guid.Empty)
                {
                    var parent = TryGetParentLinkFromQuery();
                    var created = await ApiClient.Contacts_CreateAsync(new ContactCreateRequest(
                        dto.Name,
                        dto.Type,
                        dto.CategoryId,
                        dto.Description,
                        dto.IsPaymentIntermediary,
                        parent));
                    if (created != null)
                    {
                        Id = created.Id;
                        Contact = created;
                        CardRecord = await BuildCardRecordAsync(Contact);
                        ClearPendingChanges();
                        RaiseStateChanged();
                        RaiseUiActionRequested("Saved", Id.ToString());
                    }
                }
                else
                {
                    var updated = await ApiClient.Contacts_UpdateAsync(Id, new ContactUpdateRequest(dto.Name, dto.Type, dto.CategoryId, dto.Description, dto.IsPaymentIntermediary));
                    if (updated != null) { Contact = updated; CardRecord = await BuildCardRecordAsync(Contact); ClearPendingChanges(); RaiseStateChanged(); RaiseUiActionRequested("Saved", Id.ToString()); }
                }
            }) { MobileShortcut = true },
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Contact==null, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
        });

        var linked = new UiRibbonTab(localizer["Ribbon_Group_Linked"], new List<UiRibbonAction>
        {
            new UiRibbonAction("OpenPostings", localizer["Ribbon_Postings"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Contact==null, null, () => { var url = $"/list/postings/contact/{Id}"; RaiseUiActionRequested("OpenPostings", url); return Task.CompletedTask; }),
            new UiRibbonAction("OpenMerge", localizer["Ribbon_Merge"].Value, "<svg><use href='/icons/sprite.svg#merge'/></svg>", UiRibbonItemSize.Small, Contact==null, null, () => {
                RequestOpenMerge(Id, Contact?.Type.ToString() ?? string.Empty);
                return Task.CompletedTask;
            }),
            new UiRibbonAction("Attachments", localizer["Ribbon_Attachments"].Value, "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, Contact==null, null, () => {
                RequestOpenAttachments(FinanceManager.Domain.Attachments.AttachmentEntityKind.Contact, Id);
                return Task.CompletedTask;
            })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage, linked }) };
    }

    private ContactDto BuildDto(CardRecord? record)
    {
        var name = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Contact_Name")?.Text ?? Contact?.Name ?? string.Empty;
        var typeText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Contact_Type")?.Text ?? Contact?.Type.ToString() ?? string.Empty;
        ContactType type = Contact?.Type ?? ContactType.Organization;
        if (!string.IsNullOrWhiteSpace(typeText) && Enum.TryParse<ContactType>(typeText, true, out var parsed)) type = parsed;
        var categoryId = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Contact_Category")?.ValueId ?? Contact?.CategoryId;
        var desc = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Contact_Description")?.Text ?? Contact?.Description;
        var isPaymentText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Contact_IsPaymentIntermediary")?.Text;
        var isPayment = string.Equals(isPaymentText, Localizer?["EnumType_BooleanSelection_True"].Value, StringComparison.OrdinalIgnoreCase);
        return new ContactDto(Id, name, type, categoryId, desc, isPayment, Contact?.SymbolAttachmentId);
    }

    // --- Symbol support hooks required by BaseCardViewModel ---
    /// <summary>
    /// Returns the attachment parent kind and id to be used for symbol uploads for this contact.
    /// </summary>
    /// <returns>Tuple of attachment kind and parent id.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.Contact, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Indicates whether symbol uploads are permitted for this contact. Returned true for the contact card.
    /// </summary>
    /// <returns><c>true</c> when uploads are allowed; otherwise <c>false</c>.</returns>
    protected override bool IsSymbolUploadAllowed() => true;

    /// <summary>
    /// Assigns a newly uploaded symbol attachment to the contact and refreshes the card state.
    /// Implementations should handle errors gracefully; this method preserves prior silent-failure behavior.
    /// </summary>
    /// <param name="attachmentId">The attachment id to assign, or <c>null</c> to clear the symbol.</param>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            if (attachmentId.HasValue)
            {
                await ApiClient.Contacts_SetSymbolAsync(Id, attachmentId.Value);
            }
            else
            {
                await ApiClient.Contacts_ClearSymbolAsync(Id);
            }
            await InitializeAsync(Id);
        }
        catch
        {
            // keep silent to preserve prior behavior
        }
    }
}
