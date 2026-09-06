using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Web.ViewModels.Common;
using FinanceManager.Domain.Attachments;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Budget;

/// <summary>
/// View model for the budget purpose detail card.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("budget", "purposes")]
public sealed class BudgetPurposeCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="sp">Service provider.</param>
    public BudgetPurposeCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Current purpose id.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Loaded purpose DTO.
    /// </summary>
    public BudgetPurposeDto? Purpose { get; private set; }

    /// <inheritdoc />
    public override string Title => Purpose?.Name ?? base.Title;

    /// <inheritdoc />
    public override async Task LoadAsync(Guid id)
    {
        Id = id;
        Loading = true;
        SetError(null, null);
        RaiseStateChanged();

        try
        {
            // Ensure embedded list exists.
            EmbeddedList ??= CreateSubViewModel<BudgetRuleListViewModel>();

            if (id == Guid.Empty)
            {
                var name = !string.IsNullOrWhiteSpace(InitPrefill) ? InitPrefill : string.Empty;
                Purpose = new BudgetPurposeDto(Guid.Empty, Guid.Empty, name, null, BudgetSourceType.ContactGroup, Guid.Empty, null);
                CardRecord = BuildCardRecord(Purpose);

                // No rules for a new (unsaved) purpose.
                if (EmbeddedList is BudgetRuleListViewModel rulesNew)
                {
                    await rulesNew.InitializeForPurposeAsync(Guid.Empty);
                }

                return;
            }

            Purpose = await ApiClient.Budgets_GetPurposeAsync(id);
            if (Purpose == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Budget purpose not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            CardRecord = BuildCardRecord(Purpose);

            if (EmbeddedList is BudgetRuleListViewModel rules)
            {
                await rules.InitializeForPurposeAsync(id);
            }
        }
        catch (Exception ex)
        {
            CardRecord = new CardRecord(new List<CardField>());
            SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message);
        }
        finally
        {
            Loading = false;
            RaiseStateChanged();
        }
    }

    /// <inheritdoc />
    public override async Task ReloadAsync()
    {
        await InitializeAsync(Id);
        if (EmbeddedList is BudgetRuleListViewModel rules)
        {
            await rules.InitializeForPurposeAsync(Id);
        }
    }

    private BudgetSourceType ParseSourceType(string? value, BudgetSourceType fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<BudgetSourceType>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var localizer = Localizer;
        if (localizer == null)
        {
            return fallback;
        }

        foreach (var v in new[] { BudgetSourceType.Contact, BudgetSourceType.ContactGroup, BudgetSourceType.SavingsPlan })
        {
            var key = $"EnumType_{nameof(BudgetSourceType)}_{v}";
            var localized = localizer[key];
            if (localized != null && !localized.ResourceNotFound && string.Equals(localized.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return fallback;
    }

    private static bool IsContactGroupSource(BudgetSourceType type) => type == BudgetSourceType.ContactGroup;

    private static bool IsContactSource(BudgetSourceType type) => type == BudgetSourceType.Contact;

    private static bool IsSavingsPlanSource(BudgetSourceType type) => type == BudgetSourceType.SavingsPlan;

    private BudgetSourceType ResolveSelectedBudgetSourceType(LookupItem? item, BudgetSourceType fallback)
    {
        if (item == null)
        {
            return fallback;
        }

        // Try direct parse first.
        if (Enum.TryParse<BudgetSourceType>(item.Name, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        // Otherwise fall back to matching localized display strings.
        return ParseSourceType(item.Name, fallback);
    }

    private BudgetSourceType GetCurrentSourceType(CardRecord? record)
    {
        var raw = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceType")?.Text;
        return ParseSourceType(raw, Purpose?.SourceType ?? BudgetSourceType.ContactGroup);
    }

    private BudgetValuationType ParseValuationType(string? value, BudgetValuationType fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<BudgetValuationType>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var localizer = Localizer;
        if (localizer == null)
        {
            return fallback;
        }

        foreach (var v in new[] { BudgetValuationType.ExactPostings, BudgetValuationType.TotalBudget })
        {
            var key = $"EnumType_{nameof(BudgetValuationType)}_{v}";
            var localized = localizer[key];
            if (localized != null && !localized.ResourceNotFound && string.Equals(localized.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return fallback;
    }

    private void UpdateSourceFieldLookup(CardRecord? record)
    {
        var srcField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId");
        if (srcField == null)
        {
            return;
        }

        var type = GetCurrentSourceType(record);
        if (IsContactSource(type))
        {
            srcField.LookupType = "Contact";
        }
        else if (IsContactGroupSource(type))
        {
            // Contact groups are represented as contact categories.
            srcField.LookupType = "ContactCategory";
        }
        else if (IsSavingsPlanSource(type))
        {
            srcField.LookupType = "SavingsPlan";
        }
        else
        {
            srcField.LookupType = null;
        }
    }

    private static void ClearSourceField(CardRecord? record)
    {
        var srcField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId");
        if (srcField == null)
        {
            return;
        }

        srcField.ValueId = null;
        srcField.Text = string.Empty;
    }

    private CardRecord BuildCardRecord(BudgetPurposeDto dto)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_BudgetPurpose_Name", CardFieldKind.Text, text: dto.Name ?? string.Empty, editable: true),
            new CardField("Card_Caption_BudgetPurpose_SourceType", CardFieldKind.Text, text: dto.SourceType.ToString(), editable: true, lookupType: "Enum:BudgetSourceType"),
            new CardField("Card_Caption_BudgetPurpose_SourceId", CardFieldKind.Text, text: string.Empty, editable: true),
            new CardField(
                "Card_Caption_BudgetPurpose_BudgetCategoryId",
                CardFieldKind.Text,
                text: string.Empty,
                editable: true,
                lookupType: "BudgetCategory",
                lookupField: "Name",
                valueId: dto.BudgetCategoryId,
                allowAdd: true,
                recordCreationNameSuggestion: dto.Name),
            new CardField("Card_Caption_BudgetPurpose_ValuationType", CardFieldKind.Text, text: dto.ValuationType.ToString(), editable: true, lookupType: "Enum:BudgetValuationType"),
            new CardField("Card_Caption_BudgetPurpose_Description", CardFieldKind.Text, text: dto.Description ?? string.Empty, editable: true)
        };

        var srcField = fields.First(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId");
        srcField.ValueId = dto.SourceId == Guid.Empty ? null : dto.SourceId;

        var catField = fields.First(f => f.LabelKey == "Card_Caption_BudgetPurpose_BudgetCategoryId");
        catField.ValueId = dto.BudgetCategoryId;

        var record = new CardRecord(fields, dto);
        record = ApplyPendingValues(ApplyEnumTranslations(record));

        UpdateSourceFieldLookup(record);

        // Populate display name for existing selection when no pending override exists.
        _ = TryResolveAndSetSourceNameAsync(record, dto);
        _ = TryResolveAndSetCategoryNameAsync(record, dto);

        return record;
    }

    private async Task TryResolveAndSetCategoryNameAsync(CardRecord record, BudgetPurposeDto dto)
    {
        try
        {
            if (!dto.BudgetCategoryId.HasValue || dto.BudgetCategoryId.Value == Guid.Empty)
            {
                return;
            }

            var catField = record.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_BudgetCategoryId");
            if (catField == null)
            {
                return;
            }

            if (_pendingFieldValues.ContainsKey(catField.LabelKey))
            {
                return;
            }

            var categories = await ApiClient.Budgets_ListCategoriesAsync(ct: CancellationToken.None);
            var name = categories?.FirstOrDefault(c => c.Id == dto.BudgetCategoryId.Value)?.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                catField.Text = name;
                RaiseStateChanged();
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    private async Task TryResolveAndSetSourceNameAsync(CardRecord record, BudgetPurposeDto dto)
    {
        try
        {
            if (dto.SourceId == Guid.Empty)
            {
                return;
            }

            var srcField = record.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId");
            if (srcField == null)
            {
                return;
            }

            // Do not overwrite pending value
            if (_pendingFieldValues.ContainsKey(srcField.LabelKey))
            {
                return;
            }

            string? name = null;
            switch (dto.SourceType)
            {
                case BudgetSourceType.Contact:
                    {
                        var contact = await ApiClient.Contacts_GetAsync(dto.SourceId);
                        name = contact?.Name;
                        break;
                    }
                case BudgetSourceType.ContactGroup:
                    {
                        var cats = await ApiClient.ContactCategories_ListAsync();
                        name = cats?.FirstOrDefault(c => c.Id == dto.SourceId)?.Name;
                        break;
                    }
                case BudgetSourceType.SavingsPlan:
                    {
                        var plans = await ApiClient.SavingsPlans_ListAsync(true, CancellationToken.None);
                        name = plans?.FirstOrDefault(p => p.Id == dto.SourceId)?.Name;
                        break;
                    }
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                srcField.Text = name;
                RaiseStateChanged();
            }
        }
        catch
        {
            // Best-effort only.
        }
    }

    /// <inheritdoc />
    public override async Task<IReadOnlyList<LookupItem>> QueryLookupAsync(CardField field, string? q, int skip, int take)
    {
        if (field == null)
        {
            return Array.Empty<LookupItem>();
        }

        if (field.LabelKey == "Card_Caption_BudgetPurpose_SourceId")
        {
            // Ensure lookup type matches current selected source type before querying.
            UpdateSourceFieldLookup(CardRecord);
        }

        return await base.QueryLookupAsync(field, q, skip, take);
    }

    /// <inheritdoc />
    public override void ValidateLookupField(CardField field, LookupItem? item)
    {
        if (field == null)
        {
            return;
        }

        if (field.LabelKey == "Card_Caption_BudgetPurpose_SourceType" && string.Equals(field.LookupType, "Enum:BudgetSourceType", StringComparison.OrdinalIgnoreCase))
        {
            var oldType = GetCurrentSourceType(CardRecord);
            var selectedType = ResolveSelectedBudgetSourceType(item, oldType);

            field.Text = item?.Name ?? field.Text;
            base.ValidateFieldValue(field, field.Text);

            if (selectedType != oldType)
            {
                ClearSourceField(CardRecord);
                _pendingFieldValues.Remove("Card_Caption_BudgetPurpose_SourceId");
            }

            ApplySourceFieldLookupType(CardRecord, selectedType);
            RaiseStateChanged();
            return;
        }

        if (field.LabelKey == "Card_Caption_BudgetPurpose_SourceId")
        {
            // Persist LookupItem (Key + Name) like other card VMs; ApplyPendingValues will set ValueId+Text.
            base.ValidateLookupField(field, item);
            return;
        }

        base.ValidateLookupField(field, item);
    }

    private BudgetPurposeDto BuildDto(CardRecord? record)
    {
        var name = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_Name")?.Text ?? Purpose?.Name ?? string.Empty;

        var sourceTypeText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceType")?.Text ?? Purpose?.SourceType.ToString() ?? BudgetSourceType.ContactGroup.ToString();
        var sourceType = ParseSourceType(sourceTypeText, Purpose?.SourceType ?? BudgetSourceType.ContactGroup);

        // SourceId is a lookup field -> use ValueId, not Text.
        var sourceId = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId")?.ValueId
            ?? Purpose?.SourceId
            ?? Guid.Empty;

        var categoryId = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_BudgetCategoryId")?.ValueId
            ?? Purpose?.BudgetCategoryId;

        var desc = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_Description")?.Text;
        var valuationText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_ValuationType")?.Text ?? Purpose?.ValuationType.ToString() ?? BudgetValuationType.ExactPostings.ToString();
        var valuationType = ParseValuationType(valuationText, Purpose?.ValuationType ?? BudgetValuationType.ExactPostings);

        return new BudgetPurposeDto(Id, Guid.Empty, name, desc, sourceType, sourceId, categoryId, valuationType);
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

        var linked = new UiRibbonTab(localizer["Ribbon_Group_Linked"], new List<UiRibbonAction>
        {
            new UiRibbonAction(
                "Category",
                localizer["Ribbon_BudgetCategory"].Value,
                "<svg><use href='/icons/sprite.svg#tag'/></svg>",
                UiRibbonItemSize.Small,
                Purpose?.BudgetCategoryId == null || Purpose?.BudgetCategoryId == Guid.Empty,
                null,
                () =>
                {
                    var id = Purpose?.BudgetCategoryId;
                    if (id == null || id == Guid.Empty)
                    {
                        return Task.CompletedTask;
                    }

                    RaiseUiActionRequested("Back", $"/card/budget/categories/{id.Value}");
                    return Task.CompletedTask;
                })
        });

        return new List<UiRibbonRegister>
        {
            new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage, linked })
        };
    }

    /// <inheritdoc />
    public override void ValidateFieldValue(CardField field, object? newValue)
    {
        base.ValidateFieldValue(field, newValue);

        if (field == null)
        {
            return;
        }

        if (!string.Equals(field.LabelKey, "Card_Caption_BudgetPurpose_SourceType", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // When SourceType is edited via plain text binding (enum lookup), we may receive a localized value.
        // We do not attempt to parse the localized text here; instead we update lookup type by resolving
        // the current selected type from the pending value if possible.
        var selectedType = Purpose?.SourceType ?? BudgetSourceType.ContactGroup;
        if (_pendingFieldValues.TryGetValue(field.LabelKey, out var pending) && pending is string s)
        {
            selectedType = ParseSourceType(s, selectedType);
        }

        // Clear source on any source type change.
        ClearSourceField(CardRecord);
        _pendingFieldValues.Remove("Card_Caption_BudgetPurpose_SourceId");

        ApplySourceFieldLookupType(CardRecord, selectedType);
        RaiseStateChanged();
    }

    // Rename helper to match usage
    private void ApplySourceFieldLookupType(CardRecord? record, BudgetSourceType selectedType)
    {
        var srcField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetPurpose_SourceId");
        if (srcField == null)
        {
            return;
        }

        if (IsContactSource(selectedType))
        {
            srcField.LookupType = "Contact";
            return;
        }

        if (IsContactGroupSource(selectedType))
        {
            srcField.LookupType = "ContactCategory";
            return;
        }

        if (IsSavingsPlanSource(selectedType))
        {
            srcField.LookupType = "SavingsPlan";
            return;
        }

        srcField.LookupType = null;
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
                var created = await ApiClient.Budgets_CreatePurposeAsync(new BudgetPurposeCreateRequest(dto.Name, dto.SourceType, dto.SourceId, dto.Description, dto.BudgetCategoryId, dto.ValuationType));
                Id = created.Id;
                Purpose = created;
                CardRecord = BuildCardRecord(created);
                ClearPendingChanges();
                RaiseStateChanged();

                EmbeddedList ??= new BudgetRuleListViewModel(ServiceProvider);
                if (EmbeddedList is BudgetRuleListViewModel rulesCreated)
                {
                    await rulesCreated.InitializeForPurposeAsync(Id);
                }

                RaiseUiActionRequested("Saved", Id.ToString());
                return true;
            }

            var updated = await ApiClient.Budgets_UpdatePurposeAsync(Id, new BudgetPurposeUpdateRequest(dto.Name, dto.SourceType, dto.SourceId, dto.Description, dto.BudgetCategoryId, dto.ValuationType));
            if (updated == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Update failed");
                return false;
            }

            Purpose = updated;
            CardRecord = BuildCardRecord(updated);
            ClearPendingChanges();
            RaiseStateChanged();

            EmbeddedList ??= new BudgetRuleListViewModel(ServiceProvider);
            if (EmbeddedList is BudgetRuleListViewModel rulesUpdated)
            {
                await rulesUpdated.InitializeForPurposeAsync(Id);
            }

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
            var ok = await ApiClient.Budgets_DeletePurposeAsync(Id);
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
