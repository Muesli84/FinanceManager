using System.Globalization;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;
using System.Text.Json;

namespace FinanceManager.Web.ViewModels.StatementDrafts;

/// <summary>
/// View model for a single statement draft card. Exposes draft metadata, embedded entries list
/// and provides operations to classify, validate, book and manage the draft.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("statement-drafts")]
public sealed class StatementDraftCardViewModel : BaseCardViewModel<(string Key, string Value)>, IDeletableViewModel
{
    /// <summary>
    /// Initializes a new instance of <see cref="StatementDraftCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies (API client, navigation, localizer, etc.).</param>
    public StatementDraftCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Saves quick-edit changes collected from the embedded entries list.
    /// Performs client-side validation, calls batch update API and handles per-entry errors.
    /// </summary>
    public async Task SaveQuickEditAsync()
    {
        if (DraftId == Guid.Empty) return;
        if (EmbeddedList is not StatementDraftEntriesListViewModel vm) return;

        // client-side validate changed and new rows
        var ok = vm.ValidateAllQuickEditRows();
        if (!ok)
        {
            // do not proceed if client validation fails; hints are already applied
            return;
        }

        var req = vm.CollectQuickEditSaveRequest();
        if (req.Updates.Count == 0 && req.Deletes.Count == 0 && req.Creates.Count == 0) return;

        Loading = true; RaiseStateChanged();
        try
        {
            var (success, error) = await ApiClient.StatementDrafts_BatchUpdateDetailedAsync(DraftId, req, CancellationToken.None);
            if (error != null)
            {
                if (EmbeddedList is StatementDraftEntriesListViewModel evm)
                {
                    evm.ApplyBatchValidationErrors(error);
                    // request UI focus on the first invalid row so user can correct it
                    evm.RequestFocusFirstInvalid();
                }
                // also set a generic card-level error (localized when possible)
                try
                {
                    var localizer = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
                    var msg = localizer["StatementDrafts_SaveQuickEdit_RowsFailedValidation"].Value;
                    SetError(null, string.IsNullOrWhiteSpace(msg) ? "Some rows failed validation" : msg);
                }
                catch
                {
                    // best-effort localization; fallback to a reasonable English message
                    SetError(null, "Some rows failed validation");
                }
                return;
            }

            if (success != null)
            {
                var updated = await ApiClient.StatementDrafts_GetAsync(DraftId, headerOnly: false, ct: CancellationToken.None);
                if (updated != null)
                {
                    Draft = updated;
                    // recreate embedded list to pick up latest entries
                    var entriesVm = new StatementDraftEntriesListViewModel(ServiceProvider, Draft.DraftId);
                    entriesVm.StateChanged += (_, __) => RaiseStateChanged();
                    EmbeddedList = entriesVm;
                    await entriesVm.InitializeAsync();
                }
            }

            // end quick-edit
            await vm.EndQuickEditAsync();
            RaiseUiActionRequested("Saved", DraftId.ToString());
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Cancels quick-edit session and discards local edits.
    /// </summary>
    public async Task CancelQuickEditAsync()
    {
        if (EmbeddedList is StatementDraftEntriesListViewModel vm)
        {
            await vm.EndQuickEditAsync();
            RaiseStateChanged();
        }
    }

    /// <summary>
    /// Identifier of the currently loaded draft. <see cref="Guid.Empty"/> indicates none.
    /// </summary>
    public Guid DraftId { get; private set; }

    /// <summary>
    /// Detailed DTO of the loaded draft or <c>null</c> when no draft is loaded.
    /// </summary>
    public StatementDraftDetailDto? Draft { get; private set; }

    /// <summary>
    /// Last validation result returned by a call to <see cref="ValidateAsync"/> or <see cref="BookAsync"/>
    /// when booking was withheld due to warnings/errors.
    /// </summary>
    public DraftValidationResultDto? LastValidationResult { get; private set; }

    /// <summary>
    /// Title displayed in the card header. Falls back to base title when no draft is loaded.
    /// </summary>
    public override string Title => Draft?.OriginalFileName ?? base.Title;

    /// <summary>
    /// Loads the draft with the specified <paramref name="id"/> and builds the corresponding <see cref="CardRecord"/>.
    /// When <paramref name="id"/> is <see cref="Guid.Empty"/> the view model will prepare a create-mode card.
    /// </summary>
    /// <param name="id">Identifier of the draft to load or <see cref="Guid.Empty"/> to prepare a new draft.</param>
    /// <returns>A task that completes when the load operation has finished.</returns>
    /// <exception cref="OperationCanceledException">May be thrown if underlying API calls observe a cancelled token.</exception>
    public override async Task LoadAsync(Guid id)
    {
        DraftId = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            if (id == Guid.Empty)
            {
                Draft = null;

                // Resolve localized suggested description: "Bookings from {0}" / "Buchungen vom {0}"
                string suggestedDescription = string.Empty;
                try
                {
                    var localizer = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
                    var fmt = localizer["StatementDrafts_SuggestedDescription"].Value;
                    suggestedDescription = string.Format(CultureInfo.CurrentCulture, fmt, DateTime.Now.ToString("d", CultureInfo.CurrentCulture));
                }
                catch { /* best-effort only */ }

                // When creating a new statement draft, only show a lookup field to select the target bank account.
                var createFields = new List<CardField>
                {
                    new CardField(
                        "Card_Caption_StatementDrafts_AssignedAccount",
                        CardFieldKind.Text,
                        text: string.Empty,
                        symbolId: null,
                        amount: null,
                        boolValue: null,
                        editable: true,
                        lookupType: "bankaccount",
                        lookupField: "Name",
                        valueId: null,
                        lookupFilter: null,
                        hint: null,
                        allowAdd: true),
                    // allow entering a description when creating a draft
                    new CardField(
                        "Card_Caption_StatementDrafts_Description",
                        CardFieldKind.Text,
                        text: suggestedDescription,
                        symbolId: null,
                        amount: null,
                        boolValue: null,
                        editable: true,
                        lookupType: null,
                        lookupField: null,
                        valueId: null,
                        lookupFilter: null,
                        hint: null,
                        allowAdd: false)
                };

                CardRecord = new CardRecord(createFields);
                return;
            }

            // Load full draft details so we can show counts and description
            Draft = await ApiClient.StatementDrafts_GetAsync(id, headerOnly: false, ct: CancellationToken.None);
            if (Draft == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Draft not found");
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            // Resolve detected account name for display if available
            string assignedAccountText = string.Empty;
            if (Draft.DetectedAccountId.HasValue)
            {
                try
                {
                    var acct = await ApiClient.GetAccountAsync(Draft.DetectedAccountId.Value, CancellationToken.None);
                    if (acct != null)
                    {
                        assignedAccountText = string.IsNullOrWhiteSpace(acct.Iban) ? acct.Name : $"{acct.Name} ({acct.Iban})";
                    }
                }
                catch { /* swallow - best effort only */ }
            }

            // Build card record
            // Compute sum of entry amounts for display
            var sumAmounts = Draft.Entries?.Where(e => e.Status != StatementDraftEntryStatus.AlreadyBooked && e.Status != StatementDraftEntryStatus.Announced).Sum(e => e.Amount) ?? 0m;
            var fields = new List<CardField>
            {
                // Assigned bank account lookup (editable) - show for existing drafts as well
                new CardField(
                    "Card_Caption_StatementDrafts_AssignedAccount",
                    CardFieldKind.Text,
                    text: assignedAccountText,
                    symbolId: null,
                    amount: null,
                    boolValue: null,
                    editable: true,
                    lookupType: "bankaccount",
                    lookupField: "Name",
                    valueId: Draft.DetectedAccountId,
                    lookupFilter: null,
                    hint: null,
                    allowAdd: true),

                new CardField("Card_Caption_StatementDrafts_File", CardFieldKind.Text, text: Draft.OriginalFileName ?? string.Empty),
                new CardField("Card_Caption_StatementDrafts_Description", CardFieldKind.Text, text: Draft.Description ?? string.Empty),
                new CardField("Card_Caption_StatementDrafts_Status", CardFieldKind.Text, text: Draft.Status.ToString()),
                new CardField("Card_Caption_StatementDrafts_IsPreliminary", CardFieldKind.Text, text: Draft.IsPreliminary ? (ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>()["StatementDraft_IsPreliminary"].Value ?? "Yes") : string.Empty),
                new CardField("Card_Caption_StatementDrafts_Entries", CardFieldKind.Text, text: $"{(Draft.Entries?.Count(e => e.Status != StatementDraftEntryStatus.AlreadyBooked && e.Status != StatementDraftEntryStatus.Announced) ?? 0)} ({(Draft.Entries?.Count ?? 0)})"),
                // Sum of all entry amounts
                new CardField("Card_Caption_StatementDrafts_SumAmounts", CardFieldKind.Currency, text: sumAmounts.ToString("C", CultureInfo.CurrentCulture), amount: sumAmounts)
            };

            // If this draft is assigned to an entry, show assigned amount and the difference
            if (Draft.ParentEntryId.HasValue || Draft.ParentEntryAmount.HasValue)
            {
                var assigned = Draft.ParentEntryAmount ?? 0m;
                var diff = assigned - sumAmounts;
                fields.Add(new CardField("Card_Caption_StatementDrafts_AssignedAmount", CardFieldKind.Currency, text: assigned.ToString("C", CultureInfo.CurrentCulture), amount: assigned));
                fields.Add(new CardField("Card_Caption_StatementDrafts_Difference", CardFieldKind.Currency, text: diff.ToString("C", CultureInfo.CurrentCulture), amount: diff));
            }

            CardRecord = new CardRecord(fields, Draft);

            // Expose entries via EmbeddedList which will page-load entries from API
            var entriesVm = new StatementDraftEntriesListViewModel(ServiceProvider, Draft.DraftId);
            // ensure parent card VM is notified when the embedded list state changes so Ribbon and UI update
            entriesVm.StateChanged += (_, __) => RaiseStateChanged();
            EmbeddedList = entriesVm;
            try
            {
                await entriesVm.InitializeAsync();

                if (Navigation.ToAbsoluteUri(Navigation.Uri).Query.Contains("quickEdit=true", StringComparison.OrdinalIgnoreCase))
                {
                    await entriesVm.BeginQuickEditAsync();
                }
            }
            catch (Exception ex)
            {
                SetError(null, ex.Message);
            }
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            CardRecord ??= new CardRecord(new List<CardField>());
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Deletes the current draft via the API.
    /// /// </summary>
    /// <returns>True when deletion succeeded; otherwise false.</returns>
    public override async Task<bool> DeleteAsync()
    {
        if (DraftId == Guid.Empty) return false;
        try
        {
            var ok = await ApiClient.StatementDrafts_DeleteAsync(DraftId, CancellationToken.None);
            if (ok)
                RaiseUiActionRequested("Back");
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Statement drafts do not support symbol uploads in the current model.
    /// </summary>
    /// <returns><c>false</c> always.</returns>
    protected override bool IsSymbolUploadAllowed() => false;

    /// <summary>
    /// Returns the attachment parent information for symbol assignments. For statement drafts this returns <see cref="Domain.Attachments.AttachmentEntityKind.StatementDraft"/>.
    /// </summary>
    /// <returns>Tuple of attachment kind and parent id.</returns>
    protected override SymbolParentRef GetSymbolParent()
        => new(Domain.Attachments.AttachmentEntityKind.StatementDraft, DraftId);

    /// <summary>
    /// No-op for statement draft symbols; assignment not supported.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign or <c>null</c> to clear.</param>
    protected override Task AssignNewSymbolAsync(Guid? attachmentId) => Task.CompletedTask;

    /// <summary>
    /// Builds ribbon register definitions for the draft card including navigation, manage, statement and linked tabs.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon registers or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var tabs = new List<UiRibbonTab>();

        // Navigation group
        var navItems = new List<UiRibbonAction>
        {
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, Draft == null, null, new Func<Task>(() => { RaiseUiActionRequested("Back"); return Task.CompletedTask; })) { MobileShortcut = true },
            new UiRibbonAction("Prev", localizer["Ribbon_Prev"].Value, "<svg><use href='/icons/sprite.svg#chevron-left'/></svg>", UiRibbonItemSize.Small, Draft == null || Draft.PrevInUpload == null, null, new Func<Task>(() => { RaiseUiActionRequested("Prev"); return Task.CompletedTask; })) { MobileShortcut = true },
            new UiRibbonAction("Next", localizer["Ribbon_Next"].Value, "<svg><use href='/icons/sprite.svg#chevron-right'/></svg>", UiRibbonItemSize.Small, Draft == null || Draft.NextInUpload == null, null, new Func<Task>(() => { RaiseUiActionRequested("Next"); return Task.CompletedTask; })) { MobileShortcut = true }
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, navItems));

        // Manage group
        // Manage group (primary actions)
        var manageItems = new List<UiRibbonAction>
        {
            // Save should be enabled when there are pending changes (including create-mode selections)
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !HasPendingChanges, null, new Func<Task>(async () => { await SaveAsync(); })) { MobileShortcut = true },
            new UiRibbonAction("Add", localizer["Ribbon_NewEntry"].Value, "<svg><use href='/icons/sprite.svg#new'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(() => { Navigation.NavigateTo($"/card/statement-drafts/entries/new?draftId={DraftId}"); return Task.CompletedTask; })),
            new UiRibbonAction("Book", localizer["Ribbon_Book"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(async () => { await BookAsync(); })),
            new UiRibbonAction("DeleteDraft", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(() => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })),
            new UiRibbonAction("Classify", localizer["Ribbon_Reclassify"].Value, "<svg><use href='/icons/sprite.svg#refresh'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(async () => { await ClassifyAsync(); })),
            new UiRibbonAction("Validate", localizer["Ribbon_Validate"].Value, "<svg><use href='/icons/sprite.svg#check'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(async () => { await ValidateAsync(); }))
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, manageItems));

        // QuickEdit group: toggle/save/cancel placed in their own group for clarity
        var quickEditItems = new List<UiRibbonAction>
        {
            // QuickEdit toggle — hidden while quick-edit is already active (save/cancel are shown instead)
            new UiRibbonAction("ToggleQuickEdit", localizer["Ribbon_QuickEdit"].Value, "<svg><use href='/icons/sprite.svg#edit'/></svg>", UiRibbonItemSize.Large, Draft == null, null, new Func<Task>(async () =>
            {
                if (EmbeddedList is StatementDraftEntriesListViewModel evm)
                {
                    if (evm.IsQuickEditActive)
                    {
                        await evm.EndQuickEditAsync();
                    }
                    else
                    {
                        await evm.BeginQuickEditAsync();
                    }
                    RaiseStateChanged();
                }
            })) { Hidden = EmbeddedList is StatementDraftEntriesListViewModel toggleEvm && toggleEvm.IsQuickEditActive },
            new UiRibbonAction("SaveQuickEdit", localizer["Ribbon_SaveQuickEdit"].Value, (Loading ? "<svg class='spin'><use href='/icons/sprite.svg#spinner'/></svg>" : "<svg><use href='/icons/sprite.svg#save'/></svg>"), UiRibbonItemSize.Small,
                // compute Disabled dynamically using embedded list state
                !(EmbeddedList is StatementDraftEntriesListViewModel sevm && sevm.HasPendingQuickEditChanges() && sevm.QuickEditRowsAreValid() && !Loading),
                null, new Func<Task>(async ()
                    => { await SaveQuickEditAsync(); })) { FileCallback = null, Hidden = !(EmbeddedList is StatementDraftEntriesListViewModel saveEvm && saveEvm.IsQuickEditActive) },
            new UiRibbonAction("CancelQuickEdit", localizer["Ribbon_CancelQuickEdit"].Value, "<svg><use href='/icons/sprite.svg#close'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(async () => { await CancelQuickEditAsync(); })) { Hidden = !(EmbeddedList is StatementDraftEntriesListViewModel cancelEvm && cancelEvm.IsQuickEditActive) }
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_QuickEdit"].Value, quickEditItems));
        // Additional comment for clarity

        // Statement group (Original view / download)
        var stmtItems = new List<UiRibbonAction>
        {
            new UiRibbonAction("ViewOriginal", localizer["Ribbon_ViewOriginal"].Value, "<svg><use href='/icons/sprite.svg#view'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(() => { RaiseUiActionRequested("ViewOriginal"); return Task.CompletedTask; })),
            new UiRibbonAction("DownloadOriginal", localizer["Ribbon_Download"].Value, "<svg><use href='/icons/sprite.svg#download'/></svg>", UiRibbonItemSize.Small, Draft == null, null, new Func<Task>(() => { RaiseUiActionRequested("DownloadOriginal"); return Task.CompletedTask; }))
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Statement"].Value, stmtItems));

        // Linked information group
        var linkedItems = new List<UiRibbonAction>
        {
            new UiRibbonAction("OpenAccountDetails", localizer["Ribbon_AccountDetails"].Value, "<svg><use href='/icons/sprite.svg#bank'/></svg>", UiRibbonItemSize.Small, Draft == null || Draft.DetectedAccountId == null, null, new Func<Task>(() => { RaiseUiActionRequested("OpenAccountDetails"); return Task.CompletedTask; }))
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, linkedItems));

        var registers = new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
        var baseRegs = base.GetRibbonRegisterDefinition(localizer);
        if (baseRegs != null) registers.AddRange(baseRegs);
        return registers.Count == 0 ? null : registers;
    }

    /// <summary>
    /// Requests server-side classification of the draft and updates local card and embedded entries when finished.
    /// </summary>
    public async Task ClassifyAsync()
    {
        if (DraftId == Guid.Empty) return;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var updated = await ApiClient.StatementDrafts_ClassifyAsync(DraftId, CancellationToken.None);
            if (updated == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Classification failed");
                return;
            }

            // update draft and card fields
            Draft = updated;
            var fields = new List<CardField>
            {
                new CardField("Card_Caption_StatementDrafts_File", CardFieldKind.Text, text: Draft.OriginalFileName ?? string.Empty),
                new CardField("Card_Caption_StatementDrafts_Description", CardFieldKind.Text, text: Draft.Description ?? string.Empty),
                new CardField("Card_Caption_StatementDrafts_Status", CardFieldKind.Text, text: Draft.Status.ToString()),
                new CardField("Card_Caption_StatementDrafts_Entries", CardFieldKind.Text, text: (Draft.Entries?.Count ?? 0).ToString())
            };
            CardRecord = new CardRecord(fields, Draft);

            // Recreate embedded list so it picks up new symbol/name maps
            var entriesVm = new StatementDraftEntriesListViewModel(ServiceProvider, Draft.DraftId);
            EmbeddedList = entriesVm;
            await entriesVm.InitializeAsync();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Validates the draft on the server and stores the validation result for UI rendering.
    /// </summary>
    public async Task ValidateAsync()
    {
        if (DraftId == Guid.Empty) return;
        // Clear any existing embedded panels to avoid duplicates when user triggers multiple actions
        RaiseUiActionRequested("ClearEmbeddedPanel");
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var result = await ApiClient.StatementDrafts_ValidateAsync(DraftId, CancellationToken.None);
            if (result == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Validation failed");
                LastValidationResult = null;
                return;
            }

            // Store result on viewmodel so UI can render messages on the card page
            LastValidationResult = result;
            // propagate entry-specific messages to embedded list (if present)
            if (EmbeddedList is StatementDraftEntriesListViewModel entriesVm)
            {
                entriesVm.ApplyValidationMessages(result);
            }
            // Request embedded validation panel after the ribbon
            try
            {
                var parameters = new Dictionary<string, object?> { ["ValidationResult"] = result, ["AllowProceed"] = false };
                var spec = new BaseViewModel.EmbeddedPanelSpec(typeof(FinanceManager.Web.Components.Shared.ValidationResultPanel), parameters, EmbeddedPanelPosition.AfterRibbon, true);
                RaiseUiEmbeddedPanelRequested(spec);
            }
            catch { }
            // notify UI
            RaiseStateChanged();
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }
    /// <summary>
    /// Handles the completion of the embedded panel and initiates booking if the user has confirmed.
    /// </summary>
    /// <remarks>The method checks for a confirmation flag in the provided payload. If confirmation is
    /// detected, booking is performed while ignoring warnings. Any exceptions that occur during processing are
    /// suppressed.</remarks>
    /// <param name="payloadObject">An optional object containing additional data from the embedded panel. If the object contains a Boolean property
    /// or dictionary entry named "Confirm" set to <see langword="true"/>, booking is triggered.</param>
    public override void EmbeddedPanelFinished(object? payloadObject)
    {
        base.EmbeddedPanelFinished(payloadObject);
        try
        {
            bool confirm = false;
            if (payloadObject != null)
            {
                // support anonymous object with Confirm property or a dictionary
                if (payloadObject is System.Collections.IDictionary dict)
                {
                    if (dict.Contains("Confirm") && dict["Confirm"] is bool b) confirm = b;
                }
                else
                {
                    var prop = payloadObject.GetType().GetProperty("Confirm");
                    if (prop != null && prop.PropertyType == typeof(bool))
                    {
                        confirm = (bool)(prop.GetValue(payloadObject) ?? false);
                    }
                }
            }

            if (confirm)
            {
                // user confirmed -> trigger booking while ignoring warnings
                _ = BookAsync(ignoreWarnings: true);
            }
        }
        catch
        {
            // swallow - best effort only
        }
    }

    /// <summary>
    /// Attempts to book the draft. If booking succeeds attempts to navigate to next draft or overview.
    /// If booking is withheld due to warnings the validation messages are stored in <see cref="LastValidationResult"/>.
    /// </summary>
    /// <param name="ignoreWarnings">The ignore warnings.</param>
    public async Task BookAsync(bool ignoreWarnings = false)
    {
        if (DraftId == Guid.Empty) return;
        // clear panels to avoid duplicate validation panels
        RaiseUiActionRequested("ClearEmbeddedPanel");
        Loading = true; SetError(null, null); LastValidationResult = null; RaiseStateChanged();
        try
        {
            var res = await ApiClient.StatementDrafts_BookAsync(DraftId, ignoreWarnings, CancellationToken.None);
            if (res == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Booking failed");
                return;
            }

            if (!res.Success)
            {
                // booking withheld due to warnings/errors -> show validation on card and propagate to embedded list
                LastValidationResult = res.Validation;
                if (EmbeddedList is StatementDraftEntriesListViewModel entriesVm)
                {
                    entriesVm.ApplyValidationMessages(res.Validation);
                }
                // Show validation panel after ribbon
                try
                {
                    var parameters = new Dictionary<string, object?> { ["ValidationResult"] = res.Validation, ["AllowProceed"] = true };
                    var spec = new BaseViewModel.EmbeddedPanelSpec(typeof(FinanceManager.Web.Components.Shared.ValidationResultPanel), parameters, EmbeddedPanelPosition.AfterRibbon, true);
                    RaiseUiEmbeddedPanelRequested(spec);
                }
                catch { }
                // leave UI to display warnings; do not navigate
                return;
            }

            // success -> notify UI consumers (pages) to navigate back to overview
            // After booking the current draft, try to open the next draft in the upload group.
            // If no next draft exists, navigate back to the overview.
            try
            {
                var nextDraftId = Draft?.NextInUpload;
                if (nextDraftId.HasValue && nextDraftId.Value != Guid.Empty)
                {
                    // navigate to next draft card using Saved action so CardPage builds correct card URL
                    RaiseUiActionRequested("Saved", nextDraftId.Value.ToString());
                }
                else
                {
                    // no next draft -> return to overview
                    RaiseUiActionRequested("Back", null);
                }
            }
            catch
            {
                // fallback to overview on any error
                RaiseUiActionRequested("Back", null);
            }
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Saves pending changes on the draft such as assigned account or description. When creating a new draft
    /// this method will create the draft first and then apply pending values.
    /// </summary>
    /// <returns>True when save succeeded; otherwise false.</returns>
    public override async Task<bool> SaveAsync()
    {
        // Save pending account selection for this statement draft
        try
        {
            SetError(null, null);
            // Check pending values for assigned account key or description
            var hasAccountPending = _pendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_AssignedAccount", out var pendingAccount);
            var hasDescriptionPending = _pendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Description", out var pendingDescription);
            if (!hasAccountPending && !hasDescriptionPending)
            {
                // nothing to save
                return true;
            }

            Guid? accountId = null;
            if (hasAccountPending)
            {
                if (pendingAccount is LookupItem li)
                {
                    accountId = li.Key;
                }
                else if (pendingAccount is Guid g)
                {
                    accountId = g;
                }
                else if (pendingAccount is string s && Guid.TryParse(s, out var sg))
                {
                    accountId = sg;
                }

                if (!accountId.HasValue)
                {
                    SetError(null, "No account selected");
                    return false;
                }
            }

            string? description = null;
            if (hasDescriptionPending)
            {
                if (pendingDescription is string ds)
                {
                    description = ds;
                }
                else if (pendingDescription != null)
                {
                    description = pendingDescription.ToString();
                }
            }

            // If draft is newly created and user didn't touch the description field, use the suggested description
            bool wasNew = DraftId == Guid.Empty;
            if (!hasDescriptionPending && wasNew)
            {
                try
                {
                    var localizer = ServiceProvider.GetRequiredService<IStringLocalizer<Pages>>();
                    var fmt = localizer["StatementDrafts_SuggestedDescription"].Value;
                    description = string.Format(CultureInfo.CurrentCulture, fmt, DateTime.Now.ToString("d", CultureInfo.CurrentCulture));
                }
                catch { /* best-effort only */ }
            }

            // If draft does not exist yet, create an empty draft first            
            if (wasNew)
            {
                var created = await ApiClient.StatementDrafts_CreateAsync(null, CancellationToken.None);
                if (created == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Failed to create draft");
                    return false;
                }
                Draft = created;
                DraftId = created.DraftId;
            }

            // Apply account if pending
            if (hasAccountPending && accountId.HasValue)
            {
                var updated = await ApiClient.StatementDrafts_SetAccountAsync(DraftId, accountId.Value, CancellationToken.None);
                if (updated == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Failed to set account");
                    return false;
                }
                Draft = updated;
            }

            // Apply description if pending or for newly created draft when we have a suggestion
            if ((hasDescriptionPending || wasNew) && description != null)
            {
                var updatedDesc = await ApiClient.StatementDrafts_SetDescriptionAsync(DraftId, description, CancellationToken.None);
                if (updatedDesc == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Failed to set description");
                    return false;
                }
                Draft = updatedDesc;
            }

            // refresh local state
            ClearPendingChanges();

            // If this was a newly created draft, navigate to its card view so UI shows saved state
            if (wasNew)
            {
                // Notify page to navigate to the saved card (CardPage builds URL from id)
                RaiseUiActionRequested("Saved", DraftId.ToString());
                return true;
            }

            // existing draft -> reload to reflect changes
            await LoadAsync(DraftId);
            return true;
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            return false;
        }
    }
}
