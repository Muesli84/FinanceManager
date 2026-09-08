using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Budget;

/// <summary>
/// View model for the budget rule detail card.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("budget", "rules")]
public sealed class BudgetRuleCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="sp">Service provider.</param>
    public BudgetRuleCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Current rule id.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Budget purpose id (used when creating new rules).
    /// </summary>
    public Guid BudgetPurposeId { get; private set; }

    /// <summary>
    /// Budget category id (used when creating new rules via category).
    /// </summary>
    public Guid BudgetCategoryId { get; private set; }

    /// <summary>
    /// Loaded rule DTO.
    /// </summary>
    public BudgetRuleDto? Rule { get; private set; }

    /// <inheritdoc />
    public override string Title => Rule != null ? Localizer?["General_Title_BudgetRule"].Value ?? "Rule" : base.Title;

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
                // Expect InitPrefill to contain either "purpose:{guid}" or "category:{guid}" (or legacy: just purpose guid).
                BudgetPurposeId = Guid.Empty;
                BudgetCategoryId = Guid.Empty;

                var prefill = InitPrefill ?? string.Empty;
                if (prefill.StartsWith("category:", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Guid.TryParse(prefill["category:".Length..], out var cid);
                    BudgetCategoryId = cid;
                }
                else if (prefill.StartsWith("purpose:", StringComparison.OrdinalIgnoreCase))
                {
                    _ = Guid.TryParse(prefill["purpose:".Length..], out var pid);
                    BudgetPurposeId = pid;
                }
                else
                {
                    BudgetPurposeId = Guid.TryParse(prefill, out var legacyPid) ? legacyPid : Guid.Empty;
                }

                var dto = new BudgetRuleDto(Guid.Empty, Guid.Empty, BudgetPurposeId == Guid.Empty ? null : BudgetPurposeId, BudgetCategoryId == Guid.Empty ? null : BudgetCategoryId, 0m, BudgetIntervalType.Monthly, null, DateOnly.FromDateTime(DateTime.Today), null, null, false);
                Rule = dto;
                CardRecord = BuildCardRecord(dto);
                return;
            }

            Rule = await ApiClient.Budgets_GetRuleAsync(id);
            if (Rule == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Budget rule not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            BudgetPurposeId = Rule.BudgetPurposeId ?? Guid.Empty;
            BudgetCategoryId = Rule.BudgetCategoryId ?? Guid.Empty;

            // When opening an existing rule from the embedded list we may not have a back parameter.
            // Default to navigating back to the parent budget purpose card.
            if (string.IsNullOrWhiteSpace(InitBack))
            {
                if (BudgetPurposeId != Guid.Empty)
                {
                    SetBackNavigation($"/card/budget/purposes/{BudgetPurposeId}");
                }
                else if (BudgetCategoryId != Guid.Empty)
                {
                    SetBackNavigation($"/card/budget/categories/{BudgetCategoryId}");
                }
            }

            CardRecord = BuildCardRecord(Rule);
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
    }

    private CardRecord BuildCardRecord(BudgetRuleDto dto)
    {
        var fields = new List<CardField>
        {
            new CardField("Card_Caption_BudgetRule_Amount", CardFieldKind.Currency, amount: dto.Amount, editable: true),
            new CardField("Card_Caption_BudgetRule_Interval", CardFieldKind.Text, text: dto.Interval.ToString(), editable: true, lookupType: "Enum:BudgetIntervalType"),
            new CardField("Card_Caption_BudgetRule_CustomIntervalMonths", CardFieldKind.Text, text: dto.CustomIntervalMonths?.ToString() ?? string.Empty, editable: dto.Interval == BudgetIntervalType.CustomMonths),
            new CardField("Card_Caption_BudgetRule_Start", CardFieldKind.Date, text: dto.StartDate.ToString("yyyy-MM-dd"), editable: true),
            new CardField("Card_Caption_BudgetRule_End", CardFieldKind.Date, text: dto.EndDate?.ToString("yyyy-MM-dd") ?? string.Empty, editable: true),
            new CardField("Card_Caption_BudgetRule_PurposePattern", CardFieldKind.Text, text: dto.PurposePattern ?? string.Empty, editable: true, hint: "Card_Help_BudgetRule_PurposePattern"),
            new CardField("Card_Caption_BudgetRule_UseRegex", CardFieldKind.Boolean, boolValue: dto.UseRegex, editable: true)
        };

        var record = new CardRecord(fields, dto);
        record = ApplyPendingValues(ApplyEnumTranslations(record));

        // Ensure UI state (enabled/disabled fields) matches current interval.
        ApplyIntervalDependentState(record, ParseIntervalType(record.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_Interval")?.Text, dto.Interval));

        return record;
    }

    private BudgetRuleDto BuildDto(CardRecord? record)
    {
        var amountField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_Amount");
        var amount = amountField?.Amount ?? Rule?.Amount ?? 0m;

        var intervalText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_Interval")?.Text ?? Rule?.Interval.ToString() ?? BudgetIntervalType.Monthly.ToString();
        var interval = ParseIntervalType(intervalText, Rule?.Interval ?? BudgetIntervalType.Monthly);

        int? customMonths = null;
        var customText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_CustomIntervalMonths")?.Text;
        if (interval == BudgetIntervalType.CustomMonths && int.TryParse(customText, out var months) && months > 0)
        {
            customMonths = months;
        }

        var startText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_Start")?.Text;
        var start = Rule?.StartDate ?? DateOnly.FromDateTime(DateTime.Today);
        if (!string.IsNullOrWhiteSpace(startText) && DateOnly.TryParse(startText, out var parsedStart))
        {
            start = parsedStart;
        }

        var endText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_End")?.Text;
        DateOnly? end = Rule?.EndDate;
        if (!string.IsNullOrWhiteSpace(endText) && DateOnly.TryParse(endText, out var parsedEnd))
        {
            end = parsedEnd;
        }

        var purposePatternField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_PurposePattern");
        var purposePattern = purposePatternField?.Text ?? string.Empty;
        // Allow clearing the pattern (empty string → null). Don't fall back to existing value.
        if (string.IsNullOrWhiteSpace(purposePattern))
        {
            purposePattern = null;
        }

        var useRegexField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_UseRegex");
        var useRegex = useRegexField?.BoolValue ?? Rule?.UseRegex ?? false;

        Guid? purposeId = null;
        if (BudgetPurposeId != Guid.Empty)
        {
            purposeId = BudgetPurposeId;
        }
        else if (Rule?.BudgetPurposeId.HasValue == true && Rule.BudgetPurposeId.Value != Guid.Empty)
        {
            purposeId = Rule.BudgetPurposeId.Value;
        }

        Guid? categoryId = null;
        if (BudgetCategoryId != Guid.Empty)
        {
            categoryId = BudgetCategoryId;
        }
        else if (Rule?.BudgetCategoryId.HasValue == true && Rule.BudgetCategoryId.Value != Guid.Empty)
        {
            categoryId = Rule.BudgetCategoryId.Value;
        }

        return new BudgetRuleDto(Id, Guid.Empty, purposeId, categoryId, amount, interval, customMonths, start, end, purposePattern, useRegex);
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

            if (dto.BudgetPurposeId.HasValue && dto.BudgetPurposeId.Value == Guid.Empty && dto.BudgetCategoryId.HasValue && dto.BudgetCategoryId.Value == Guid.Empty)
            {
                SetError(null, Localizer?["Err_Invalid_budgetPurposeId"].Value ?? "Budget purpose/category is missing");
                return false;
            }

            if (Id == Guid.Empty)
            {
                if (dto.BudgetPurposeId.HasValue && dto.BudgetPurposeId.Value != Guid.Empty)
                {
                    var created = await ApiClient.Budgets_CreateRuleAsync(new BudgetRuleCreateRequest(dto.BudgetPurposeId, null, dto.Amount, dto.Interval, dto.CustomIntervalMonths, dto.StartDate, dto.EndDate, dto.PurposePattern, dto.UseRegex));
                    Id = created.Id;
                    Rule = created;
                    BudgetPurposeId = created.BudgetPurposeId ?? Guid.Empty;
                    BudgetCategoryId = created.BudgetCategoryId ?? Guid.Empty;
                    CardRecord = BuildCardRecord(created);
                    ClearPendingChanges();
                    RaiseStateChanged();
                    RaiseUiActionRequested("Saved", Id.ToString());
                    return true;
                }

                if (dto.BudgetCategoryId.HasValue && dto.BudgetCategoryId.Value != Guid.Empty)
                {
                    var created = await ApiClient.Budgets_CreateRuleAsync(new BudgetRuleCreateRequest(null, dto.BudgetCategoryId, dto.Amount, dto.Interval, dto.CustomIntervalMonths, dto.StartDate, dto.EndDate, dto.PurposePattern, dto.UseRegex));
                    Id = created.Id;
                    Rule = created;
                    BudgetPurposeId = created.BudgetPurposeId ?? Guid.Empty;
                    BudgetCategoryId = created.BudgetCategoryId ?? Guid.Empty;
                    CardRecord = BuildCardRecord(created);
                    ClearPendingChanges();
                    RaiseStateChanged();
                    RaiseUiActionRequested("Saved", Id.ToString());
                    return true;
                }

                SetError(null, Localizer?["Err_Invalid_budgetPurposeId"].Value ?? "Budget purpose/category is missing");
                return false;
            }

            var updated = await ApiClient.Budgets_UpdateRuleAsync(Id, new BudgetRuleUpdateRequest(dto.Amount, dto.Interval, dto.CustomIntervalMonths, dto.StartDate, dto.EndDate, dto.PurposePattern, dto.UseRegex));
            if (updated == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Update failed");
                return false;
            }

            Rule = updated;
            BudgetPurposeId = updated.BudgetPurposeId ?? Guid.Empty;
            BudgetCategoryId = updated.BudgetCategoryId ?? Guid.Empty;
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

        if (!await ConfirmationService.ConfirmAsync(new(
            TitleResourceKey: "Confirmation_Delete_Title",
            MessageResourceKey: "Confirmation_Delete_Message",
            Severity: ConfirmationSeverity.Critical)))
        {
            return false;
        }

        Loading = true;
        SetError(null, null);
        RaiseStateChanged();

        try
        {
            var ok = await ApiClient.Budgets_DeleteRuleAsync(Id);
            if (!ok)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed");
                return false;
            }
            if (!NavigateBackExtended())
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

    private bool NavigateBackExtended()
    {
        if (!string.IsNullOrWhiteSpace(InitBack))
        {
            RaiseUiActionRequested("Back", InitBack);
            return true;
        }
        else if (BudgetPurposeId != Guid.Empty)
        {
            RaiseUiActionRequested("Back", $"/card/budget/purposes/{BudgetPurposeId}");
            return true;
        }
        else return false;
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
            new UiRibbonAction(
                "Back",
                localizer["Ribbon_Back"].Value,
                "<svg><use href='/icons/sprite.svg#back'/></svg>",
                UiRibbonItemSize.Large,
                false,
                null,
                () =>
                {
                    if (!string.IsNullOrWhiteSpace(InitBack))
                    {
                        RaiseUiActionRequested("Back", InitBack);
                    }
                    else if (BudgetPurposeId != Guid.Empty)
                    {
                        RaiseUiActionRequested("Back", $"/card/budget/purposes/{BudgetPurposeId}");
                    }
                    else
                    {
                        RaiseUiActionRequested("Back");
                    }

                    return Task.CompletedTask;
                })
        });

        var manage = new UiRibbonTab(localizer["Ribbon_Group_Manage"], new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !HasPendingChanges, null, async () => { await SaveAsync(); }) { MobileShortcut = true },
            new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Id == Guid.Empty, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
        });

        var clearPurposePattern = new UiRibbonAction(
            "ClearPurposePattern",
            localizer["Ribbon_ClearPurposePattern"].Value ?? "Clear Pattern",
            "<svg><use href='/icons/sprite.svg#delete'/></svg>",
            UiRibbonItemSize.Small,
            Rule?.PurposePattern == null,
            null,
            () =>
            {
                ClearPurposePattern();
                return Task.CompletedTask;
            });

        var pattern = new UiRibbonTab(localizer["Ribbon_Group_Fields"], new List<UiRibbonAction> { clearPurposePattern });

        return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, new List<UiRibbonTab> { nav, manage, pattern }) };
    }

    private BudgetIntervalType ParseIntervalType(string? value, BudgetIntervalType fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<BudgetIntervalType>(value, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        var localizer = Localizer;
        if (localizer == null)
        {
            return fallback;
        }

        foreach (var v in new[] { BudgetIntervalType.Monthly, BudgetIntervalType.Quarterly, BudgetIntervalType.Yearly, BudgetIntervalType.CustomMonths })
        {
            var key = $"EnumType_{nameof(BudgetIntervalType)}_{v}";
            var localized = localizer[key];
            if (localized != null && !localized.ResourceNotFound && string.Equals(localized.Value, value, StringComparison.OrdinalIgnoreCase))
            {
                return v;
            }
        }

        return fallback;
    }

    private BudgetIntervalType GetCurrentInterval(CardRecord? record)
    {
        var raw = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_Interval")?.Text;
        return ParseIntervalType(raw, Rule?.Interval ?? BudgetIntervalType.Monthly);
    }

    private BudgetIntervalType ResolveSelectedInterval(LookupItem? item, BudgetIntervalType fallback)
    {
        if (item == null)
        {
            return fallback;
        }

        // Try direct parse first, then localized match.
        return ParseIntervalType(item.Name, fallback);
    }

    /// <inheritdoc />
    public override void ValidateLookupField(CardField field, LookupItem? item)
    {
        if (field == null)
        {
            return;
        }

        if (field.LabelKey == "Card_Caption_BudgetRule_Interval" && string.Equals(field.LookupType, "Enum:BudgetIntervalType", StringComparison.OrdinalIgnoreCase))
        {
            var oldInterval = GetCurrentInterval(CardRecord);
            var selected = ResolveSelectedInterval(item, oldInterval);

            field.Text = item?.Name ?? field.Text;
            base.ValidateFieldValue(field, field.Text);

            ApplyIntervalDependentState(CardRecord, selected);
            RaiseStateChanged();
            return;
        }

        base.ValidateLookupField(field, item);
    }

    /// <inheritdoc />
    public override void ValidateFieldValue(CardField field, object? newValue)
    {
        base.ValidateFieldValue(field, newValue);

        if (field == null)
        {
            return;
        }

        if (!string.Equals(field.LabelKey, "Card_Caption_BudgetRule_Interval", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var selected = Rule?.Interval ?? BudgetIntervalType.Monthly;
        if (_pendingFieldValues.TryGetValue(field.LabelKey, out var pending) && pending is string s)
        {
            selected = ParseIntervalType(s, selected);
        }

        ApplyIntervalDependentState(CardRecord, selected);
        RaiseStateChanged();
    }

    private void ApplyIntervalDependentState(CardRecord? record, BudgetIntervalType interval)
    {
        var customField = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_CustomIntervalMonths");
        if (customField == null)
        {
            return;
        }

        var shouldEnable = interval == BudgetIntervalType.CustomMonths;
        customField.Editable = shouldEnable;

        if (!shouldEnable)
        {
            customField.Text = string.Empty;
            _pendingFieldValues.Remove(customField.LabelKey);
        }
    }

    /// <summary>
    /// Clears the purpose pattern and regex flag, then marks changes as pending.
    /// </summary>
    private void ClearPurposePattern()
    {
        var patternField = CardRecord?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_PurposePattern");
        var regexField = CardRecord?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_BudgetRule_UseRegex");

        if (patternField != null)
        {
            patternField.Text = string.Empty;
            _pendingFieldValues[patternField.LabelKey] = string.Empty;
        }

        if (regexField != null)
        {
            regexField.BoolValue = false;
            _pendingFieldValues[regexField.LabelKey] = false;
        }

        RaiseStateChanged();
    }
}
