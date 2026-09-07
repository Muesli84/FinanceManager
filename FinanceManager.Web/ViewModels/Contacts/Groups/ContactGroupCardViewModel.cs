using FinanceManager.Shared;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.ViewModels.Common;

namespace FinanceManager.Web.ViewModels.Contacts.Groups;


/// <summary>
/// View model for contact category (group) card. Supports loading, creating, updating and deleting a single contact category
/// and provides symbol upload support.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("contacts", "categories")]
public sealed class ContactGroupCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{

    /// <summary>
    /// Initializes a new instance of <see cref="ContactGroupCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve required services such as the API client and localizer.</param>
    public ContactGroupCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded contact category.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Local edit model representing the contact category being edited.
    /// </summary>
    /// <returns>The result.</returns>
    public EditModel Model { get; } = new();

    /// <summary>
    /// Title shown in the card header. Falls back to the model name or base title when not available.
    /// </summary>
    /// <returns>The result.</returns>
    public override string Title
    {
        get
        {
            return CardRecord?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_ContactCategory_Name")?.Text ?? (Model?.Name ?? base.Title);
        }
    }

    /// <summary>
    /// Loads the contact category with the specified identifier. When <paramref name="id"/> is <see cref="Guid.Empty"/>
    /// a new model is prepared for creation.
    /// </summary>
    /// <param name="id">Identifier of the category to load, or <see cref="Guid.Empty"/> to initialize a new category.</param>
    /// <returns>A task that completes when loading has finished and the card record is prepared.</returns>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                // new
                Model.Name = string.Empty;
                Model.SymbolAttachmentId = null;
            }
            else
            {
                var dto = await ApiClient.ContactCategories_GetAsync(id);
                if (dto is null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Not found");
                    CardRecord = new CardRecord(new List<CardField>());
                    Loading = false; RaiseStateChanged();
                    return;
                }
                Model.Name = dto.Name ?? string.Empty;
                Model.SymbolAttachmentId = dto.SymbolAttachmentId;
            }

            CardRecord = BuildCardRecord(Model.Name, Model.SymbolAttachmentId);
            ClearPendingChanges();
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
            CardRecord = new CardRecord(new List<CardField>());
        }
        finally { Loading = false; RaiseStateChanged(); }
    }

    /// <summary>
    /// Builds the card record for the contact category using the supplied name and symbol id.
    /// </summary>
    /// <param name="name">Display name of the category.</param>
    /// <param name="symbolId">Optional symbol attachment id.</param>
    /// <returns>A <see cref="CardRecord"/> representing the category for rendering in the card UI.</returns>
    private CardRecord BuildCardRecord(string name, Guid? symbolId)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_ContactCategory_Name", CardFieldKind.Text, text: name ?? string.Empty, editable: true),
            new CardField("Card_Caption_ContactCategory_Symbol", CardFieldKind.Symbol, symbolId: symbolId, editable: true)
        };
        return new CardRecord(fields, Model);
    }

    /// <summary>
    /// Persists pending changes to the contact category. Creates a new category when <see cref="Id"/> is empty, otherwise updates.
    /// </summary>
    /// <returns>A task that resolves to <c>true</c> when the save succeeded; otherwise <c>false</c> when an error occurred.</returns>
    public override async Task<bool> SaveAsync()
    {
        // apply pending values from CardRecord into Model
        if (CardRecord != null)
        {
            var nameField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_ContactCategory_Name");
            if (nameField != null) Model.Name = nameField.Text ?? string.Empty;
            var symField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_ContactCategory_Symbol");
            if (symField != null) Model.SymbolAttachmentId = symField.SymbolId;
        }

        try
        {
            if (Id == Guid.Empty)
            {
                var created = await ApiClient.ContactCategories_CreateAsync(new ContactCategoryCreateRequest(Model.Name));
                if (created is null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Create failed");
                    return false;
                }
                Id = created.Id;
                Model.SymbolAttachmentId = created.SymbolAttachmentId;
            }
            else
            {
                var ok = await ApiClient.ContactCategories_UpdateAsync(Id, new ContactCategoryUpdateRequest(Model.Name));
                if (!ok)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Update failed");
                    return false;
                }
            }

            await LoadAsync(Id);
            RaiseUiActionRequested("Saved", Id.ToString());
            ClearPendingChanges();
            return true;
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Deletes the contact category represented by this card.
    /// </summary>
    /// <returns>A task that resolves to <c>true</c> when the delete succeeded; otherwise <c>false</c>.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (Id == Guid.Empty) return false;
        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return false;
        }

        try
        {
            var ok = await ApiClient.ContactCategories_DeleteAsync(Id);
            if (!ok)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed");
                return false;
            }
            RaiseUiActionRequested("Deleted");
            return true;
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Reloads the current category by reloading the card with the current Id.
    /// </summary>
    public override async Task ReloadAsync()
    {
        await LoadAsync(Id);
    }

    /// <summary>
    /// Returns ribbon register definitions used by the card UI including navigation and manage groups.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels for the ribbon actions.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances describing available actions.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(Microsoft.Extensions.Localization.IStringLocalizer localizer)
    {
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        var canSave = CardRecord != null && HasPendingChanges;

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !canSave, null, async () => { await SaveAsync(); }) { MobileShortcut = true },
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Id == Guid.Empty, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage }) };
    }

    // --- Symbol support hooks required by BaseCardViewModel ---
    /// <summary>
    /// Returns the attachment parent kind and id to be used for symbol uploads for this contact category.
    /// </summary>
    /// <returns>Tuple of attachment kind and parent id.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.ContactCategory, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Indicates whether symbol uploads are permitted for this contact category. Returns <c>true</c>.
    /// </summary>
    /// <returns><c>true</c> when uploads are allowed; otherwise <c>false</c>.</returns>
    protected override bool IsSymbolUploadAllowed() => true;

    /// <summary>
    /// Assigns a newly uploaded symbol attachment to the contact category and refreshes the card state.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign, or <c>null</c> to clear the symbol.</param>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            if (attachmentId.HasValue)
            {
                await ApiClient.ContactCategories_SetSymbolAsync(Id, attachmentId.Value);
            }
            else
            {
                await ApiClient.ContactCategories_ClearSymbolAsync(Id);
            }
            await LoadAsync(Id);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Local edit model used to represent the category state in the card UI.
    /// </summary>
    public sealed class EditModel
    {
        /// <summary>
        /// Category display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Optional attachment id used as the category symbol.
        /// </summary>
        public Guid? SymbolAttachmentId { get; set; }
    }
}
