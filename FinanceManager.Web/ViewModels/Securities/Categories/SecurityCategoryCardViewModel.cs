using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.ViewModels.Securities.Categories;

/// <summary>
/// View model for a security category card. Supports loading, creating, updating,
/// deleting and symbol attachment operations for a single security category.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("securities", "categories")]
public sealed class SecurityCategoryCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Initializes a new instance of <see cref="SecurityCategoryCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider to resolve required services like the API client.</param>
    public SecurityCategoryCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded security category. <see cref="Guid.Empty"/> when creating a new category.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Edit model holding the values bound to the UI when creating or editing a category.
    /// </summary>
    /// <returns>The result.</returns>
    public EditModel Model { get; } = new();

    /// <summary>
    /// Computed title for the card derived from the category name field or the edit model.
    /// </summary>
    /// <returns>The result.</returns>
    public override string Title
    {
        get
        {
            return CardRecord?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_SecurityCategory_Name")?.Text ?? (Model?.Name ?? base.Title);
        }
    }

    /// <summary>
    /// Loads the security category identified by <paramref name="id"/>. When <see cref="Guid.Empty"/>
    /// the view model is prepared for creating a new category and any initial prefill is applied.
    /// </summary>
    /// <param name="id">Category identifier to load or <see cref="Guid.Empty"/> to initialize a new category.</param>
    /// <returns>A task that completes when the load operation has finished.</returns>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                Model.Name = InitPrefill ?? string.Empty;
                Model.SymbolAttachmentId = null;
            }
            else
            {
                var dto = await ApiClient.SecurityCategories_GetAsync(id);
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
    /// Builds a <see cref="CardRecord"/> containing the editable fields for this category.
    /// </summary>
    /// <param name="name">Category display name.</param>
    /// <param name="symbolId">Optional attachment id used as symbol.</param>
    /// <returns>A <see cref="CardRecord"/> instance ready for rendering.</returns>
    private CardRecord BuildCardRecord(string name, Guid? symbolId)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_SecurityCategory_Name", CardFieldKind.Text, text: name ?? string.Empty, editable: true),
            new CardField("Card_Caption_SecurityCategory_Symbol", CardFieldKind.Symbol, symbolId: symbolId, editable: true)
        };
        return new CardRecord(fields, Model);
    }

    /// <summary>
    /// Saves the current category by creating or updating it via the API.
    /// </summary>
    /// <returns><c>true</c> when save succeeded; otherwise <c>false</c> and <see cref="FinanceManager.Web.ViewModels.Common.BaseViewModel.SetError(string?, string?)"/> provides details.</returns>
    public override async Task<bool> SaveAsync()
    {
        if (CardRecord != null)
        {
            var nameField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_SecurityCategory_Name");
            if (nameField != null) Model.Name = nameField.Text ?? string.Empty;
            var symField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_SecurityCategory_Symbol");
            if (symField != null) Model.SymbolAttachmentId = symField.SymbolId;
        }

        try
        {
            if (Id == Guid.Empty)
            {
                var created = await ApiClient.SecurityCategories_CreateAsync(new SecurityCategoryRequest { Name = Model.Name });
                if (created == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Create failed");
                    return false;
                }
                Id = created.Id;
                Model.SymbolAttachmentId = created.SymbolAttachmentId;
            }
            else
            {
                var dto = await ApiClient.SecurityCategories_UpdateAsync(Id, new SecurityCategoryRequest { Name = Model.Name });
                if (dto == null)
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
    /// Deletes the current category via the API.
    /// </summary>
    /// <returns><c>true</c> when deletion succeeded; otherwise <c>false</c> and error details are available via the view model.</returns>
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
            var ok = await ApiClient.SecurityCategories_DeleteAsync(Id);
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
    /// Reloads the currently loaded category.
    /// </summary>
    public override async Task ReloadAsync()
        => await LoadAsync(Id);

    /// <summary>
    /// Builds ribbon register definitions for the security category card including navigation and manage actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon registers describing available tabs and actions.</returns>
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

    /// <summary>
    /// Returns the parent information used for symbol attachments.
    /// </summary>
    /// <returns>Attachment entity kind and the parent id used when uploading symbols.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.SecurityCategory, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Indicates whether symbol upload is allowed in the current state. Categories always allow symbol uploads.
    /// </summary>
    /// <returns><c>true</c> when symbol upload is allowed.</returns>
    protected override bool IsSymbolUploadAllowed() => true;

    /// <summary>
    /// Assigns a newly uploaded symbol (attachment) to the category. Updates the server and reloads the category; exceptions are swallowed.
    /// </summary>
    /// <param name="attachmentId">Attachment id of the uploaded symbol, or <c>null</c> to clear the symbol.</param>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            if (attachmentId.HasValue)
            {
                await ApiClient.SecurityCategories_SetSymbolAsync(Id, attachmentId.Value);
            }
            else
            {
                await ApiClient.SecurityCategories_ClearSymbolAsync(Id);
            }
            await LoadAsync(Id);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Edit model used to gather user input for the security category.
    /// </summary>
    public sealed class EditModel
    {
        /// <summary>Category display name.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Attachment id used as category symbol.</summary>
        public Guid? SymbolAttachmentId { get; set; }
    }
}
