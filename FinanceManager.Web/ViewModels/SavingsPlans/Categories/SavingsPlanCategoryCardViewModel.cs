using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.SavingsPlans;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.DependencyInjection;
using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.ViewModels.SavingsPlans.Categories;

/// <summary>
/// View model for the savings plan category card. Manages loading, creating, updating and deleting
/// a savings plan category and supports symbol attachment operations.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("savings-plans", "categories")]
public sealed class SavingsPlanCategoryCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Initializes a new instance of <see cref="SavingsPlanCategoryCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies such as the API client and localizer.</param>
    public SavingsPlanCategoryCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the category. <see cref="Guid.Empty"/> indicates a new (unsaved) entry.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Current name of the category.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Attachment id of the assigned symbol or <c>null</c> when none is assigned.
    /// </summary>
    public Guid? SymbolId { get; private set; }

    /// <summary>
    /// Card title shown in the UI. Falls back to base title when <see cref="Name"/> is not set.
    /// </summary>
    public override string Title => Name ?? base.Title;

    /// <summary>
    /// Loads the category by id and prepares the card's <see cref="CardRecord"/> for rendering.
    /// When <paramref name="id"/> is <see cref="Guid.Empty"/>, prepares an empty model for creation.
    /// </summary>
    /// <param name="id">Identifier of the category to load, or <see cref="Guid.Empty"/> for create mode.</param>
    /// <returns>A <see cref="Task"/> that completes when loading has finished.</returns>
    /// <exception cref="OperationCanceledException">May be thrown if underlying API calls are cancelled.</exception>
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                Name = string.Empty; SymbolId = null;
            }
            else
            {
                var dto = await ApiClient.SavingsPlanCategories_GetAsync(id);
                if (dto == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Not found");
                    CardRecord = new CardRecord(new List<CardField>());
                    Loading = false; RaiseStateChanged();
                    return;
                }
                Name = dto.Name ?? string.Empty;
                SymbolId = dto.SymbolAttachmentId;
            }

            CardRecord = BuildCardRecord(Name, SymbolId);
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
    /// Builds a <see cref="CardRecord"/> for the category from the supplied values and current pending edits.
    /// </summary>
    /// <param name="name">Name to show on the card.</param>
    /// <param name="symbolId">Symbol attachment id to show on the card (may be <c>null</c>).</param>
    /// <returns>A populated <see cref="CardRecord"/> instance used by the UI.</returns>
    private CardRecord BuildCardRecord(string name, Guid? symbolId)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_SavingsPlanCategory_Name", CardFieldKind.Text, text: name ?? string.Empty, editable: true),
            new CardField("Card_Caption_SavingsPlanCategory_Symbol", CardFieldKind.Symbol, symbolId: symbolId, editable: true)
        };
        return new CardRecord(fields, new { Name = name, SymbolId = symbolId });
    }

    /// <summary>
    /// Persists the current card values by creating or updating the category via the API.
    /// Pending edits are applied to the request before sending.
    /// </summary>
    /// <returns>Returns <c>true</c> when the save succeeded; otherwise <c>false</c>.</returns>
    public override async Task<bool> SaveAsync()
    {
        // apply pending values
        if (CardRecord != null)
        {
            var nameField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_SavingsPlanCategory_Name");
            if (nameField != null) Name = nameField.Text ?? string.Empty;
            var symField = CardRecord.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_SavingsPlanCategory_Symbol");
            if (symField != null) SymbolId = symField.SymbolId;
        }

        try
        {
            if (Id == Guid.Empty)
            {
                var created = await ApiClient.SavingsPlanCategories_CreateAsync(new SavingsPlanCategoryDto { Name = Name });
                if (created == null) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Create failed"); return false; }
                Id = created.Id; SymbolId = created.SymbolAttachmentId;
            }
            else
            {
                var updated = await ApiClient.SavingsPlanCategories_UpdateAsync(Id, new SavingsPlanCategoryDto { Id = Id, Name = Name });
                if (updated == null) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Update failed"); return false; }
                SymbolId = updated.SymbolAttachmentId;
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
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (Id == Guid.Empty) return false;
        try
        {
            var ok = await ApiClient.SavingsPlanCategories_DeleteAsync(Id);
            if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed"); return false; }
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
    /// Reloads the entity by re-invoking <see cref="LoadAsync(Guid)"/> for the current Id.
    /// </summary>
    public override async Task ReloadAsync()
        => await LoadAsync(Id);

    /// <summary>
    /// Builds ribbon register definitions for the savings plan category card including navigation and manage actions.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve labels for ribbon actions.</param>
    /// <returns>A list of <see cref="UiRibbonRegister"/> instances representing available ribbon tabs and actions.</returns>
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
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Id==Guid.Empty, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage }) };
    }

    // Symbol support
    /// <summary>
    /// Returns the attachment parent kind and id used for symbol uploads. The returned <see cref="AttachmentEntityKind"/> is <see cref="AttachmentEntityKind.SavingsPlanCategory"/>.
    /// </summary>
    /// <returns>Tuple of attachment kind and parent id (or <see cref="Guid.Empty"/>).</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.SavingsPlanCategory, Id == Guid.Empty ? Guid.Empty : Id);

    /// <summary>
    /// Indicates whether symbol upload is allowed for this card. Always returns true; the actual API will validate the operation.
    /// </summary>
    /// <returns><c>true</c> when symbol uploads are permitted.</returns>
    protected override bool IsSymbolUploadAllowed() => true;

    /// <summary>
    /// Assigns or clears the symbol attachment for the current category by calling the API and reloading the entity.
    /// Exceptions are swallowed to keep UI behavior consistent; callers can inspect <see cref="ApiClient.LastError"/> for details.
    /// </summary>
    /// <param name="attachmentId">Id of the attachment to assign, or <c>null</c> to clear the symbol.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            if (attachmentId.HasValue) await ApiClient.SavingsPlanCategories_SetSymbolAsync(Id, attachmentId.Value);
            else await ApiClient.SavingsPlanCategories_ClearSymbolAsync(Id);
            await LoadAsync(Id);
        }
        catch { }
    }
}
