using FinanceManager.Domain.Attachments;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.ViewModels.Accounts
{
    // Card VM: builds key/value pairs for a single bank account

    /// <summary>
    /// View model for the account detail card displayed in the UI.
    /// Responsible for loading a single account, building the card record and handling save / delete actions.
    /// </summary>
    [FinanceManager.Web.ViewModels.Common.CardRoute("accounts")]
    public sealed class BankAccountCardViewModel : BaseCardViewModel<(string Key, string Value)>, FinanceManager.Web.ViewModels.Common.IDeletableViewModel
    {
        /// <summary>
        /// Initializes a new instance of <see cref="BankAccountCardViewModel"/>.
        /// </summary>
        /// <param name="sp">Service provider used to resolve required services such as the API client and localizer.</param>
        public BankAccountCardViewModel(IServiceProvider sp)
            : base(sp)
        {
        }

        /// <summary>
        /// Identifier of the currently loaded account.
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// DTO representing the currently loaded account or <c>null</c> when none is loaded.
        /// </summary>
        public AccountDto? Account { get; private set; }

        /// <summary>
        /// Current list of linked sub-IBANs for the collection account.
        /// </summary>
        /// <returns>The result.</returns>
        public IReadOnlyList<string> LinkedIbans { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// Input value for the new IBAN to add to the collection account.
        /// </summary>
        public string NewLinkedIban { get; set; } = string.Empty;

        /// <summary>
        /// Effective value of the IsCollectionAccount flag, taking pending edits into account.
        /// Returns the pending value if the user has toggled the checkbox without saving, otherwise the saved value.
        /// </summary>
        public bool EffectiveIsCollectionAccount =>
            _pendingFieldValues.TryGetValue("Card_Caption_Account_IsCollectionAccount", out var pv) && pv is bool b
                ? b
                : Account?.IsCollectionAccount ?? false;

        /// <summary>
        /// Card title shown in the UI. Falls back to the base title when no account is loaded.
        /// </summary>
        public override string Title => Account?.Name ?? base.Title;

        /// <summary>
        /// Loads account data and builds the card record for the specified <paramref name="id"/>.
        /// When <paramref name="id"/> equals <see cref="Guid.Empty"/> a new blank DTO is prepared for creation.
        /// </summary>
        /// <param name="id">Account identifier to load.</param>
        /// <returns>A task that completes when loading has finished. The view model state (Loading/Error/CardRecord) is updated accordingly.</returns>
        public override async Task LoadAsync(Guid id)
        {
            Id = id;
            Loading = true; SetError(null, null); RaiseStateChanged();
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            try
            {
                if (id == Guid.Empty)
                {
                    Account = new AccountDto(Guid.Empty, string.Empty, AccountType.Giro, null, 0m, Guid.Empty, null, SavingsPlanExpectation.Optional, true);
                    CardRecord = await BuildCardRecordsAsync(Account);
                    return;
                }

                Account = await api.GetAccountAsync(id);
                if (Account == null)
                {
                    SetError(api.LastErrorCode ?? null, api.LastError ?? "Account not found");
                    CardRecord = new CardRecord(new List<CardField>());
                    return;
                }
                LinkedIbans = Account.LinkedIbans;
                CardRecord = await BuildCardRecordsAsync(Account);
            }
            catch (Exception ex)
            {
                CardRecord = new CardRecord(new List<CardField>());
                SetError(api.LastErrorCode ?? null, api.LastError ?? ex.Message);
            }
            finally { Loading = false; RaiseStateChanged(); }
        }

        /// <summary>
        /// Deletes the currently loaded account.
        /// </summary>
        /// <returns>
        /// A task that resolves to <c>true</c> when the account was successfully deleted; otherwise <c>false</c>.
        /// </returns>
        public override async Task<bool> DeleteAsync()
        {
            if (Account == null) return false;
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
                var ok = await ApiClient.DeleteAccountAsync(Id);
                if (!ok) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Delete failed"); return false; }
                RaiseUiActionRequested("Deleted");
                return true;
            }
            catch (Exception ex) { SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? ex.Message); return false; }
            finally { Loading = false; RaiseStateChanged(); }
        }

        /// <summary>
        /// Provides an optional chart view model used by the UI to show account aggregates.
        /// </summary>
        public override AggregateBarChartViewModel? ChartViewModel
        {
            get
            {
                var title = Localizer?["Chart_Title_Account_Aggregates"] ?? "Account";
                var endpoint = $"/api/accounts/{Id}/aggregates";
                return new AggregateBarChartViewModel(ServiceProvider, endpoint, title);
            }
        }

        private async Task<CardRecord> BuildCardRecordsAsync(AccountDto a)
        {
            // Build record from authoritative source (Account + contact lookup). Pending values are applied afterwards via ApplyPendingValues.
            var bankContactName = string.Empty;
            if (a.BankContactId != Guid.Empty)
            {
                try
                {
                    var api = ServiceProvider.GetRequiredService<IApiClient>();
                    var c = await api.Contacts_GetAsync(a.BankContactId);
                    if (c != null) bankContactName = c.Name;
                }
                catch
                {
                    // ignore
                }
            }

            var fields = new List<CardField>
            {
                new CardField("Card_Caption_Account_Name", CardFieldKind.Text, text: a.Name ?? string.Empty, editable: true),
                new CardField("Card_Caption_Account_Iban", CardFieldKind.Text, text: a.Iban ?? string.Empty, symbolId: null, editable: true),
                new CardField("Card_Caption_Account_Type", CardFieldKind.Text, text: a.Type.ToString(), editable: (a.Id == Guid.Empty), lookupType: "Enum:AccountType"),
                new CardField("Card_Caption_Account_Balance", CardFieldKind.Currency, amount: a.CurrentBalance),
                new CardField("Card_Caption_Account_Symbol", CardFieldKind.Symbol, symbolId: a.SymbolAttachmentId, editable: a.Id != Guid.Empty),
                new CardField("Card_Caption_Account_Contact", CardFieldKind.Text, text: bankContactName ?? "-", editable: true, lookupType: "Contact", lookupField: "Name", valueId: a.BankContactId, lookupFilter: "Type=Bank"),
                new CardField("Card_Caption_Account_SavingsPlanExpectation", CardFieldKind.Text, text: a.SavingsPlanExpectation.ToString(), editable: true, lookupType: "Enum:SavingsPlanExpectation"),
                new CardField("Card_Caption_Account_SecurityProcessing", CardFieldKind.Boolean, boolValue: a.SecurityProcessingEnabled, editable: true),
                new CardField("Card_Caption_Account_IsCollectionAccount", CardFieldKind.Boolean, boolValue: a.IsCollectionAccount, editable: true)
            };

            var record = new CardRecord(fields, a);
            record = ApplyEnumTranslations(record);
            return ApplyPendingValues(record);
        }

        private AccountDto BuildDto(CardRecord? record)
        {
            var name = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_Name")?.Text ?? Account?.Name ?? string.Empty;
            var iban = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_Iban")?.Text ?? Account?.Iban ?? string.Empty;
            var typeText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_Type")?.Text;
            AccountType type = Account?.Type ?? AccountType.Giro;
            if (!string.IsNullOrWhiteSpace(typeText) && Enum.TryParse<AccountType>(typeText, ignoreCase: true, out var parsedType))
            {
                type = parsedType;
            }

            var spText = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_SavingsPlanExpectation")?.Text;
            var enumType = typeof(SavingsPlanExpectation);
            SavingsPlanExpectation spExpectation = Account?.SavingsPlanExpectation ?? SavingsPlanExpectation.Optional;
            if (!string.IsNullOrWhiteSpace(spText))
            {
                foreach (var v in Enum.GetValues<SavingsPlanExpectation>())
                {
                    var key = $"EnumType_{enumType.Name}_{v}";
                    var val = Localizer?[key];
                    if (val != null && !string.IsNullOrWhiteSpace(val.Value) && string.Equals(val.Value, spText, StringComparison.OrdinalIgnoreCase))
                    {
                        spExpectation = v;
                        break;
                    }
                }
                if (!Enum.TryParse<SavingsPlanExpectation>(spText, ignoreCase: true, out var parsedSp))
                {
                    // parsedSp not used
                }
                else
                {
                    spExpectation = parsedSp;
                }
            }

            var symbolId = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_Symbol")?.SymbolId ?? Account?.SymbolAttachmentId;
            var bankContactId = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_Contact")?.ValueId ?? Account?.BankContactId ?? Guid.Empty;
            var securityProcessing = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_SecurityProcessing")?.BoolValue
                ?? Account?.SecurityProcessingEnabled
                ?? true;

            var isCollectionAccount = record?.Fields.FirstOrDefault(f => f.LabelKey == "Card_Caption_Account_IsCollectionAccount")?.BoolValue
                ?? Account?.IsCollectionAccount
                ?? false;

            return new AccountDto(Account?.Id ?? Guid.Empty, name, type, string.IsNullOrWhiteSpace(iban) ? null : iban, Account?.CurrentBalance ?? 0m, bankContactId, symbolId, spExpectation, securityProcessing)
            {
                Name = name,
                Type = type,
                Iban = string.IsNullOrWhiteSpace(iban) ? null : iban,
                SymbolAttachmentId = symbolId,
                BankContactId = bankContactId,
                SavingsPlanExpectation = spExpectation,
                SecurityProcessingEnabled = securityProcessing,
                IsCollectionAccount = isCollectionAccount,
                CurrentBalance = Account?.CurrentBalance ?? 0m,
                Id = Account?.Id ?? Guid.Empty
            };
        }

        /// <summary>
        /// Returns the ribbon actions/tabs available for this card view and their labels (localized by the provided <paramref name="localizer"/>).
        /// </summary>
        /// <param name="localizer">Localizer used to resolve UI labels.</param>
        /// <returns>A list of <see cref="UiRibbonRegister"/> instances describing available UI actions.</returns>
        protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
        {
            // Group: Navigieren (Back)
            var navActions = new List<UiRibbonAction>
            {
                new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, false, null, () => { RaiseUiActionRequested("Back"); return Task.CompletedTask; }) { MobileShortcut = true }
            };

            // Group: Verwalten (Save, Reset, Delete)
            var manageActions = new List<UiRibbonAction>
            {
                new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Large, !HasPendingChanges, null, async () => { await SavePendingAsync(); }) { MobileShortcut = true },
                new UiRibbonAction("Reset", localizer["Ribbon_Reset"].Value, "<svg><use href='/icons/sprite.svg#undo'/></svg>", UiRibbonItemSize.Small, !HasPendingChanges, null, async () => { await ResetPendingAsync(); }) { MobileShortcut = true },
                new UiRibbonAction("Delete", localizer["Ribbon_Delete"].Value, "<svg><use href='/icons/sprite.svg#trash'/></svg>", UiRibbonItemSize.Small, Account == null || Account.Id == Guid.Empty, null, () => { RaiseUiActionRequested("Delete"); return Task.CompletedTask; })
            };

            // Group: Verkn�pfte Informationen (OpenPostings, OpenBankContact, OpenAttachments)
            var linkedActions = new List<UiRibbonAction>
            {
                new UiRibbonAction("OpenPostings", localizer["Ribbon_OpenPostings"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Account == null, null, () => {
                    // Provide target URL as payload so the page doesn't need to know ViewModel type
                    var url = $"/list/postings/account/{Id}";
                    RaiseUiActionRequested("OpenPostings", url);
                    return Task.CompletedTask;
                }),
                new UiRibbonAction("OpenBankContact", localizer["Ribbon_OpenBankContact"].Value, "<svg><use href='/icons/sprite.svg#bank'/></svg>", UiRibbonItemSize.Small, Account == null || Account.BankContactId == Guid.Empty, null, () => {
                    if (Account != null && Account.BankContactId != Guid.Empty)
                    {
                        var payload = $"contacts,{Account.BankContactId}";
                        RaiseUiActionRequested("OpenBankContact", payload);
                    }
                    return Task.CompletedTask;
                }),
                new UiRibbonAction("OpenAttachments", localizer["Ribbon_Attachments"].Value, "<svg><use href='/icons/sprite.svg#attachment'/></svg>", UiRibbonItemSize.Small, Account == null, null, () => {
                    // Request attachments overlay with ParentKind/ParentId payload
                    RequestOpenAttachments(FinanceManager.Domain.Attachments.AttachmentEntityKind.Account, Id);
                    return Task.CompletedTask;
                })
            };

            // Group: Buchungen (CreatePreliminaryDraft)
            var bookingActions = new List<UiRibbonAction>
            {
                new UiRibbonAction("CreatePreliminaryDraft", localizer["Ribbon_CreatePreliminaryDraft"].Value ?? "Vorläufige Buchungen erfassen", "<svg><use href='/icons/sprite.svg#edit'/></svg>", UiRibbonItemSize.Large, Account == null || Account.Id == Guid.Empty, null, async () => {
                    var request = new FinanceManager.Shared.Dtos.Statements.CreatePreliminaryStatementDraftRequest(Id);
                    var draft = await ApiClient.StatementDrafts_CreatePreliminaryAsync(request);
                    if (draft == null)
                    {
                        SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? "Create preliminary draft failed");
                        RaiseStateChanged();
                        return;
                    }
                    var url = $"/card/statement-drafts/{draft.DraftId}?quickEdit=true";
                    RaiseUiActionRequested("OpenStatementDraft", url);
                })
                { MobileShortcut = true }
            };

            var tabs = new List<UiRibbonTab>
            {
                new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, navActions),
                new UiRibbonTab(localizer["Ribbon_Group_Manage"].Value, manageActions),
                new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, linkedActions),
                new UiRibbonTab(localizer["Ribbon_Group_Bookings"].Value ?? "Buchungen", bookingActions)
            };

            return new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
        }

        private async Task ResetPendingAsync()
        {
            // discard pending changes and restore card from authoritative Account
            try
            {
                ClearPendingChanges();
                if (Account != null)
                {
                    CardRecord = await BuildCardRecordsAsync(Account);
                }
            }
            catch
            {
                // ignore
            }
            RaiseStateChanged();
        }

        private async Task SavePendingAsync()
        {
            // Keep a snapshot of UI state so we can restore on error
            var prevAccount = Account;
            var prevRecord = CardRecord;

            Loading = true;
            SetError(null, null);
            RaiseStateChanged();
            var api = ServiceProvider.GetRequiredService<IApiClient>();
            try
            {
                if (_pendingFieldValues.Count == 0) return;

                var newDto = BuildDto(CardRecord);

                if (Id == Guid.Empty)
                {
                    // Create new account
                    var createReq = new AccountCreateRequest(
                        newDto.Name,
                        newDto.Type,
                        newDto.Iban,
                        newDto.BankContactId == Guid.Empty ? null : newDto.BankContactId,
                        null,
                        newDto.SymbolAttachmentId,
                        newDto.SavingsPlanExpectation,
                        newDto.SecurityProcessingEnabled,
                        newDto.IsCollectionAccount);

                    var created = await api.CreateAccountAsync(createReq);
                    if (created == null)
                    {
                        SetError(api.LastErrorCode ?? null, api.LastError ?? "Create failed");
                        Account = prevAccount;
                        CardRecord = prevRecord ?? new CardRecord(new List<CardField>());
                        ClearPendingChanges();
                        return;
                    }

                    Id = created.Id;
                    Account = created;
                    CardRecord = await BuildCardRecordsAsync(Account);
                    ClearPendingChanges();

                    RaiseUiActionRequested("Saved", Id.ToString());
                    return;
                }

                // existing account: update
                var req = new FinanceManager.Shared.Dtos.Accounts.AccountUpdateRequest(
                    newDto.Name,
                    newDto.Type,
                    newDto.Iban,
                    newDto.BankContactId,
                    null,
                    newDto.SymbolAttachmentId,
                    newDto.SavingsPlanExpectation,
                    newDto.SecurityProcessingEnabled,
                    false,
                    newDto.IsCollectionAccount);

                var updated = await api.UpdateAccountAsync(Id, req);

                if (updated == null)
                {
                    SetError(api.LastErrorCode ?? null, api.LastError ?? "Update failed");
                    Account = prevAccount;
                    CardRecord = prevRecord ?? new CardRecord(new List<CardField>());
                    ClearPendingChanges();
                    return;
                }

                Account = updated;
                CardRecord = await BuildCardRecordsAsync(Account);
                ClearPendingChanges();
                RaiseUiActionRequested("Saved");
            }
            catch (Exception ex)
            {
                SetError(api.LastErrorCode ?? null, api.LastError ?? ex.Message);
                Account = prevAccount;
                CardRecord = prevRecord ?? new CardRecord(new List<CardField>());
            }
            finally
            {
                Loading = false;
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// Reloads the current card by re-initializing with the current Id.
        /// </summary>
        public override async Task ReloadAsync()
        {
            await InitializeAsync(Id);
        }

        private Type? ResolveEnumType(string enumName)
        {
            // Try known namespace first
            var candidates = new[] {
                $"FinanceManager.Shared.Dtos.Accounts.{enumName}",
                enumName
            };
            foreach (var n in candidates)
            {
                try
                {
                    var t = Type.GetType(n, throwOnError: false, ignoreCase: true);
                    if (t != null && t.IsEnum) return t;
                }
                catch { }
            }

            // Search loaded assemblies
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                try
                {
                    var t = asm.GetTypes().FirstOrDefault(x => x.IsEnum && (string.Equals(x.Name, enumName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.FullName, enumName, StringComparison.OrdinalIgnoreCase) || x.FullName?.EndsWith("." + enumName, StringComparison.OrdinalIgnoreCase) == true));
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        // --- Symbol support hooks required by BaseCardViewModel ---
        /// <summary>
        /// Returns the attachment parent information used to upload or list symbols for this card.
        /// </summary>
        /// <returns>Tuple of attachment kind and parent id.</returns>
        protected override SymbolParentRef GetSymbolParent()
        => new(AttachmentEntityKind.Account, Id == Guid.Empty ? Guid.Empty : Id);

        /// <summary>
        /// Indicates whether symbol upload is allowed for the current account.
        /// </summary>
        /// <returns><c>true</c> when upload is permitted; otherwise <c>false</c>.</returns>
        protected override bool IsSymbolUploadAllowed() => Id != Guid.Empty;

        /// <summary>
        /// Assigns or clears the symbol attachment for this account and refreshes the card state.
        /// </summary>
        /// <param name="attachmentId">Attachment identifier to assign, or <c>null</c> to clear the symbol.</param>
        /// <returns>A task that completes when the assignment operation has finished.</returns>
        protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
        {
            try
            {
                var api = ServiceProvider.GetRequiredService<IApiClient>();
                if (attachmentId.HasValue)
                {
                    await api.SetAccountSymbolAsync(Id, attachmentId.Value);
                }
                else
                {
                    await api.ClearAccountSymbolAsync(Id);
                }
                await InitializeAsync(Id);
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Adds the IBAN entered in <see cref="NewLinkedIban"/> to the collection account and refreshes the IBAN list.
        /// </summary>
        /// <returns>A task that completes when the operation has finished. Returns true on success.</returns>
        public async Task<bool> AddLinkedIbanAsync()
        {
            if (string.IsNullOrWhiteSpace(NewLinkedIban)) return false;
            SetError(null, null);
            try
            {
                var api = ServiceProvider.GetRequiredService<IApiClient>();
                await api.AddLinkedIbanAsync(Id, new AccountLinkedIbanUpsertRequest(NewLinkedIban.Replace(" ", "")));
                NewLinkedIban = string.Empty;
                LinkedIbans = await api.GetLinkedIbansAsync(Id);
                RaiseStateChanged();
                return true;
            }
            catch (Exception ex)
            {
                var api2 = ServiceProvider.GetRequiredService<IApiClient>();
                var errorCode = api2.LastErrorCode;
                string? errorMsg;
                if (errorCode == "Err_Iban_Duplicate")
                {
                    errorMsg = Localizer?["LinkedIbans_Error_AlreadyLinked"]?.Value
                        ?? api2.LastError
                        ?? ex.Message;
                }
                else
                {
                    errorMsg = api2.LastError ?? ex.Message;
                }
                SetError(errorCode, errorMsg);
                RaiseStateChanged();
                return false;
            }
        }

        /// <summary>
        /// Removes the specified IBAN from the collection account and refreshes the IBAN list.
        /// </summary>
        /// <param name="iban">The IBAN to remove.</param>
        /// <returns>A task that completes when the operation has finished.</returns>
        public async Task RemoveLinkedIbanAsync(string iban)
        {
            SetError(null, null);
            try
            {
                var api = ServiceProvider.GetRequiredService<IApiClient>();
                await api.RemoveLinkedIbanAsync(Id, iban);
                LinkedIbans = await api.GetLinkedIbansAsync(Id);
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                SetError(null, ex.Message);
                RaiseStateChanged();
            }
        }
    }
}
