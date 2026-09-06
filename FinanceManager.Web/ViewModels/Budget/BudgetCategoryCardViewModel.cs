using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Budget;

/// <summary>
/// View model for the budget category detail card.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("budget", "categories")]
public sealed class BudgetCategoryCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public BudgetCategoryCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Current category id.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Loaded category DTO.
    /// </summary>
    public BudgetCategoryDto? Category { get; private set; }

    /// <inheritdoc />
    public override string Title => Category?.Name ?? base.Title;

    // EmbeddedList is provided by BaseCardViewModel (set it when category is loaded)

    /// <inheritdoc />
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true;
        SetError(null, null);
        RaiseStateChanged();

        try
        {
            if (id == Guid.Empty)
            {
                var name = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill : string.Empty;
                Category = new BudgetCategoryDto(Guid.Empty, Guid.Empty, name);
                CardRecord = BuildCardRecord(Category);
                EmbeddedList = null;
                return;
            }

            Category = await ApiClient.Budgets_GetCategoryAsync(id);
            if (Category == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Budget category not found");
                CardRecord = new CardRecord(new List<CardField>());
                EmbeddedList = null;
                return;
            }

            CardRecord = BuildCardRecord(Category);

            var rules = CreateSubViewModel<BudgetRuleListViewModel>(singletonPerType: true);
            await rules.InitializeForCategoryAsync(id);
            EmbeddedList = rules;
        }
        catch (Exception ex)
        {
            CardRecord = new CardRecord(new List<CardField>());
            EmbeddedList = null;
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public override Task ReloadAsync() => InitializeAsync(Id);

    private CardRecord BuildCardRecord(BudgetCategoryDto dto)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_BudgetCategory_Name", CardFieldKind.Text, text: dto.Name ?? string.Empty, editable: true)
        };

        var record = new CardRecord(fields, dto);
        record = ApplyPendingValues(record);
        return record;
    }

    private BudgetCategoryDto BuildDto(CardRecord? record)
    {
        var name = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetCategory_Name")?.Text
            ?? Category?.Name
            ?? string.Empty;

        return new BudgetCategoryDto(Id, Guid.Empty, name);
    }

    /// <inheritdoc />
    protected override bool IsSymbolUploadAllowed() => false;

    /// <inheritdoc />
    protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.None, Guid.Empty);

    /// <inheritdoc />
    protected override Task AssignNewSymbolAsync(Guid? attachmentId) => Task.CompletedTask;

    /// <inheritdoc />
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var nav = new UiRibbonTab(localizer["Ribbon_Group_Navigation"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
        });

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !HasPendingChanges, null, async () => { await SaveAsync(); }) { MobileShortcut = true },
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Id == Guid.Empty, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
        });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage }) };
    }

    /// <inheritdoc />
    public override async Task<bool> SaveAsync()
    {
        Loading = true;
        SetError(null, null);
        RaiseStateChanged();

        try
        {
            var dto = BuildDto(CardRecord);

            if (Id == Guid.Empty)
            {
                var parent = TryGetParentLinkFromQuery();

                var created = await ApiClient.Budgets_CreateCategoryAsync(new BudgetCategoryCreateRequest(dto.Name, parent));
                Id = created.Id;
                Category = created;
                CardRecord = BuildCardRecord(created);
                ClearPendingChanges();
                RaiseStateChanged();

                RaiseUiActionRequested("Saved", Id.ToString());
                return true;
            }

            var updated = await ApiClient.Budgets_UpdateCategoryAsync(Id, new BudgetCategoryUpdateRequest(dto.Name));
            if (updated == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Update failed");
                return false;
            }

            Category = updated;
            CardRecord = BuildCardRecord(updated);
            ClearPendingChanges();
            RaiseStateChanged();
            RaiseUiActionRequested("Saved", Id.ToString());
            return true;
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
            return false;
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public override async Task<bool> DeleteAsync()
    {
        if (Id == Guid.Empty)
        {
            return false;
        }

        Loading = true;
        SetError(null, null);
        RaiseStateChanged();

        try
        {
            var ok = await ApiClient.Budgets_DeleteCategoryAsync(Id);
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
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }
}
