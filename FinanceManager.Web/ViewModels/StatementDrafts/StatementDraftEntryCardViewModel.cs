using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Shared;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Web.ViewModels.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using FinanceManager.Shared.Dtos.Securities;
using FinanceManager.Shared.Dtos.Accounts;
using FinanceManager.Shared.Dtos.Contacts;

namespace FinanceManager.Web.ViewModels.StatementDrafts;

/// <summary>
/// View model for a single statement draft entry card. Responsible for loading, editing, validating and booking
/// individual statement draft entries and exposing card fields and ribbon actions to the UI.
/// </summary>
[FinanceManager.Web.ViewModels.Common.CardRoute("statement-drafts", "entries")]
public sealed class StatementDraftEntryCardViewModel : BaseCardViewModel<(string Key, string Value)>
{
    private StatementDraftEntryDetailDto? _entryDetail;

    // Mode: when false core fields are read-only; when true core fields editable
    private bool _isEditMode = false;
    /// <summary>
    /// Indicates whether the card is currently in edit mode. When false core booking fields are read-only.
    /// </summary>
    public bool IsEditMode => _isEditMode;

    // cached display names so toggling edit mode can rebuild UI without further API calls
    private string _contactName = string.Empty;
    private string _savingsPlanName = string.Empty;
    private string _securityName = string.Empty;
    private bool _contactIsSelf = false;
    private bool _contactIsPaymentIntermediary = false;
    private bool _accountAllowsSavings = true;
    private bool _accountAllowsSecurity = true;

    /// <summary>
    /// Initializes a new instance of <see cref="StatementDraftEntryCardViewModel"/>.
    /// </summary>
    /// <param name="sp">Service provider used to resolve dependencies (API client, navigation, localizer etc.).</param>
    public StatementDraftEntryCardViewModel(IServiceProvider sp) : base(sp)
    {
    }

    /// <summary>
    /// Identifier of the currently loaded entry. <see cref="Guid.Empty"/> indicates a new (unsaved) entry.
    /// </summary>
    public Guid EntryId { get; private set; }

    /// <summary>
    /// Identifier of the parent draft that this entry belongs to.
    /// </summary>
    public Guid DraftId { get; private set; }

    /// <summary>
    /// DTO representing the current entry or <c>null</c> when creating a new entry.
    /// </summary>
    public StatementDraftEntryDto? Entry { get; private set; }

    /// <summary>
    /// Title shown on the card header; uses the entry subject when available.
    /// </summary>
    public override string Title => Entry?.Subject ?? base.Title;

    // Helper to build the CardField list from the current Entry and cached lookup names
    private List<CardField> BuildFields()
    {
        var entry = Entry!; // caller must ensure Entry != null

        // Localize status text via resources if available
        var localizer = ServiceProvider.GetService<IStringLocalizer<Pages>>();
        var statusKey = $"EnumType_StatementDraftEntryStatus_{entry.Status}";
        var statusText = localizer != null ? localizer[statusKey].Value : entry.Status.ToString();

        // If an entry is announced or already booked, it must not be editable at all for core fields.
        var isLocked = entry.IsAnnounced || entry.Status == StatementDraftEntryStatus.AlreadyBooked;
        var isEditableCore = _isEditMode && !isLocked;

        var fields = new List<CardField>
        {
            new CardField("Card_Caption_StatementDrafts_Date", CardFieldKind.Date, text: entry.BookingDate.ToString("d", CultureInfo.CurrentCulture), editable: isEditableCore),
            new CardField("Card_Caption_StatementDrafts_Valuta", CardFieldKind.Date, text: entry.ValutaDate?.ToString("d", CultureInfo.CurrentCulture), editable: isEditableCore),
            new CardField("Card_Caption_StatementDrafts_Amount", CardFieldKind.Currency, text: entry.Amount.ToString(CultureInfo.CurrentCulture), amount: entry.Amount, editable: isEditableCore),
            new CardField("Card_Caption_StatementDrafts_BookingDescription", CardFieldKind.Text, text: entry.BookingDescription ?? string.Empty, editable: isEditableCore),
            new CardField("Card_Caption_StatementDrafts_Recipient", CardFieldKind.Text, text: entry.RecipientName ?? string.Empty, editable: isEditableCore),
            new CardField("Card_Caption_StatementDrafts_Subject", CardFieldKind.Text, text: entry.Subject ?? string.Empty, editable: isEditableCore),
            // Announced must always be read-only
            new CardField("Card_Caption_StatementDrafts_Announced", CardFieldKind.Boolean, boolValue: entry.IsAnnounced, editable: false),
            new CardField("Card_Caption_StatementDrafts_Status", CardFieldKind.Text, text: statusText),

            // Contact lookup - editable unless locked
            new CardField("Card_Caption_StatementDrafts_Contact", CardFieldKind.Text, text: _contactName, editable: !isLocked, lookupType: "Contact", lookupField: "Name", valueId: entry.ContactId, allowAdd: true, recordCreationNameSuggestion: entry.RecipientName),

            // Cost-neutral flag - editable unless locked
            new CardField("Card_Caption_StatementDrafts_CostNeutral", CardFieldKind.Boolean, boolValue: entry.IsCostNeutral, editable: !isLocked),
        };

        // Conditionally include savings plan field when contact is Self and account allows savings
        if (_contactIsSelf && _accountAllowsSavings)
        {
            var insertIndex = 9; // after Contact and CostNeutral by default ordering
            if (insertIndex > fields.Count) insertIndex = fields.Count;
            fields.Insert(insertIndex, new CardField("Card_Caption_StatementDrafts_SavingsPlan", CardFieldKind.Text, text: _savingsPlanName, editable: true, lookupType: "SavingsPlan", lookupField: "Name", valueId: entry.SavingsPlanId, allowAdd: true, recordCreationNameSuggestion: entry.Subject));
        }

        // Security-related fields are only shown when the selected contact equals the bank contact of the statement draft
        var contactIsBankContact = (_entryDetail?.BankContactId.HasValue == true && entry.ContactId.HasValue && _entryDetail!.BankContactId == entry.ContactId);
        var hasSecurityData = entry.SecurityId is not null && entry.SecurityId != Guid.Empty
            || entry.SecurityTransactionType.HasValue
            || entry.SecurityQuantity.HasValue
            || entry.SecurityFeeAmount.HasValue
            || entry.SecurityTaxAmount.HasValue;
        if ((_accountAllowsSecurity && contactIsBankContact) || hasSecurityData)
        {
            var securityEditable = _accountAllowsSecurity && !isLocked && contactIsBankContact;
            fields.Add(new CardField("Card_Caption_StatementDrafts_Security", CardFieldKind.Text, text: _securityName, editable: securityEditable, lookupType: "Security", lookupField: "Name", valueId: entry.SecurityId, allowAdd: true, recordCreationNameSuggestion: entry.Subject));
            // Show localized enum label so the lookup's localized names match and the dropdown selection displays correctly
            string? txText = null;
            try
            {
                if (entry.SecurityTransactionType.HasValue)
                {
                    var enumName = entry.SecurityTransactionType.Value.ToString();
                    var key = $"EnumType_SecurityTransactionType_{enumName}";
                    var loc = ServiceProvider.GetService<IStringLocalizer<Pages>>()?[key];
                    if (loc != null && !loc.ResourceNotFound && !string.IsNullOrWhiteSpace(loc.Value)) txText = loc.Value;
                    else txText = enumName;
                }
            }
            catch { txText = entry.SecurityTransactionType?.ToString(); }
            fields.Add(new CardField("Card_Caption_StatementDrafts_TransactionType", CardFieldKind.Text, text: txText, editable: securityEditable, lookupType: "Enum:SecurityTransactionType"));
            fields.Add(new CardField("Card_Caption_StatementDrafts_Quantity", CardFieldKind.Text, text: entry.SecurityQuantity?.ToString(), editable: securityEditable));
            fields.Add(new CardField("Card_Caption_StatementDrafts_Fee", CardFieldKind.Currency, text: entry.SecurityFeeAmount?.ToString(), amount: entry.SecurityFeeAmount, editable: securityEditable));
            fields.Add(new CardField("Card_Caption_StatementDrafts_Tax", CardFieldKind.Currency, text: entry.SecurityTaxAmount?.ToString(), amount: entry.SecurityTaxAmount, editable: securityEditable));
        }

        // If this entry is associated with a split/group draft, show assigned amount and difference immediately after Amount
        try
        {
            if (_entryDetail?.SplitSum.HasValue == true)
            {
                var assigned = _entryDetail.SplitSum.Value;
                var diff = _entryDetail.Difference;
                var insertIndex = 3; // after Date, Valuta, Amount
                if (insertIndex > fields.Count) insertIndex = fields.Count;
                fields.Insert(insertIndex, new CardField("Card_Caption_StatementDrafts_AssignedAmount", CardFieldKind.Currency, text: assigned.ToString("C", CultureInfo.CurrentCulture), amount: assigned));
                fields.Insert(insertIndex + 1, new CardField("Card_Caption_StatementDrafts_Difference", CardFieldKind.Currency, text: (diff ?? 0m).ToString("C", CultureInfo.CurrentCulture), amount: diff));
            }
        }
        catch { /* ignore formatting failures */ }

        return fields;
    }

    /// <summary>
    /// Toggles edit mode for the card. Rebuilds the card UI to reflect new editability rules.
    /// Edit mode cannot be entered for announced or already booked entries.
    /// </summary>
    /// <returns>A completed task when the toggle has been applied.</returns>
    public Task ToggleEditModeAsync()
    {
        // Prevent entering edit mode for announced entries
        if (!_isEditMode)
        {
            if (Entry == null)
            {
                return Task.CompletedTask;
            }
            if (Entry.IsAnnounced)
            {
                SetError(null, "Cannot enter edit mode for announced entries.");
                return Task.CompletedTask;
            }
            if (Entry.Status == StatementDraftEntryStatus.AlreadyBooked)
            {
                SetError(null, "Entry already booked - reset status first to allow editing.");
                return Task.CompletedTask;
            }
        }

        _isEditMode = !_isEditMode;
        try
        {
            if (Entry != null)
            {
                CardRecord = new CardRecord(BuildFields(), Entry);
                RaiseStateChanged();
            }
        }
        catch { /* ignore UI rebuild failures */ }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the entry with the specified <paramref name="id"/> and prepares card state for display/editing.
    /// When <paramref name="id"/> is <see cref="Guid.Empty"/> the view model will prepare create-mode fields for a new entry.
    /// </summary>
    /// <param name="id">Identifier of the entry to load or <see cref="Guid.Empty"/> to prepare a new entry.</param>
    /// <returns>A task that completes when loading has finished.</returns>
    public override async Task LoadAsync(Guid id)
    {
        EntryId = id;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            // try to pick up draftId from query string so callers can navigate to this card
            DraftId = Guid.Empty;
            try
            {
                var nav = ServiceProvider.GetRequiredService<NavigationManager>();
                var uri = nav.ToAbsoluteUri(nav.Uri);
                var q = QueryHelpers.ParseQuery(uri.Query);
                if (q.TryGetValue("draftId", out var v) && Guid.TryParse(v, out var d)) DraftId = d;
            }
            catch { /* ignore */ }

            if (EntryId == Guid.Empty)
            {
                // New entry mode for a given draft: show editable fields to create an entry
                Entry = null;
                // Reset any previously loaded entry detail so a stale, non-null _entryDetail
                // (from a prior LoadAsync(existingId) call on this same view model instance)
                // doesn't make SaveAsync() incorrectly treat this as an "update existing entry"
                // flow, which would dereference the now-null Entry property.
                _entryDetail = null;
                _isEditMode = true;

                // Suggested defaults
                var suggestedDate = DateTime.Now.Date;
                var suggestedAmount = 0m;
                var suggestedSubject = string.Empty;
                var suggestedBookingDescription = Localizer?["StatementDraftEntry_SuggestedBookingDescription"].Value;

                var createFieldsNew = new List<CardField>
                {
                    new CardField("Card_Caption_StatementDrafts_Date", CardFieldKind.Date, text: suggestedDate.ToString("d", CultureInfo.CurrentCulture), editable: true),
                    new CardField("Card_Caption_StatementDrafts_Valuta", CardFieldKind.Date, text: suggestedDate.ToString("d", CultureInfo.CurrentCulture), editable: true),
                    new CardField("Card_Caption_StatementDrafts_Amount", CardFieldKind.Currency, text: suggestedAmount.ToString(CultureInfo.CurrentCulture), amount: suggestedAmount, editable: true),
                    new CardField("Card_Caption_StatementDrafts_Recipient", CardFieldKind.Text, text: string.Empty, editable: true),
                    new CardField("Card_Caption_StatementDrafts_Subject", CardFieldKind.Text, text: suggestedSubject, editable: true),
                    new CardField("Card_Caption_StatementDrafts_BookingDescription", CardFieldKind.Text, text: suggestedBookingDescription, editable: true)
                };

                CardRecord = new CardRecord(createFieldsNew);
                Loading = false; RaiseStateChanged();
                return;
            }

            var dto = await ApiClient.StatementDrafts_GetEntryAsync(DraftId, EntryId, CancellationToken.None);
            if (dto == null)
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Entry not found");
                Entry = null;
                CardRecord = new CardRecord(new List<CardField>());
                return;
            }

            _entryDetail = dto;
            Entry = dto.Entry;
            DraftId = dto.DraftId;

            // No client-side assignment required: new linked entities are assigned server-side during their creation.

            // Resolve lookup display names (Contact, SavingsPlan, Security)
            if (Entry.ContactId.HasValue)
            {
                try
                {
                    var contactDto = await ApiClient.Contacts_GetAsync(Entry.ContactId.Value, CancellationToken.None);
                    _contactName = contactDto?.Name ?? string.Empty;
                    _contactIsSelf = contactDto?.Type == ContactType.Self;
                    _contactIsPaymentIntermediary = contactDto?.IsPaymentIntermediary == true;
                }
                catch { _contactName = string.Empty; }
            }

            // Try to detect account settings via draft header
            try
            {
                var draftHeader = await ApiClient.StatementDrafts_GetAsync(DraftId, headerOnly: true, src: null, fromEntryDraftId: null, fromEntryId: null, CancellationToken.None);
                if (draftHeader?.DetectedAccountId is Guid acctId)
                {
                    var detectedAccount = await ApiClient.GetAccountAsync(acctId, CancellationToken.None);
                    if (detectedAccount != null)
                    {
                        _accountAllowsSavings = detectedAccount.SavingsPlanExpectation != SavingsPlanExpectation.None;
                        _accountAllowsSecurity = detectedAccount.SecurityProcessingEnabled;
                    }
                }
            }
            catch { /* ignore */ }

            if (Entry.SavingsPlanId.HasValue)
            {
                try
                {
                    var sp = await ApiClient.SavingsPlans_GetAsync(Entry.SavingsPlanId.Value, CancellationToken.None);
                    _savingsPlanName = sp?.Name ?? string.Empty;
                }
                catch { _savingsPlanName = string.Empty; }
            }

            if (Entry.SecurityId.HasValue)
            {
                try
                {
                    var sx = await ApiClient.Securities_GetAsync(Entry.SecurityId.Value, CancellationToken.None);
                    _securityName = sx?.Name ?? string.Empty;
                }
                catch { _securityName = string.Empty; }
            }

            // Build card fields: many are editable/lookups and will be persisted by SaveAsync
            var fields = BuildFields();

            // Keep raw enum names in CardRecord.Text so editable selects can bind to them correctly
            CardRecord = new CardRecord(fields, Entry);
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            CardRecord = new CardRecord(new List<CardField>());
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Saves pending changes for the current entry. Supports both creation of new entries and updating existing entries.
    /// </summary>
    /// <returns>True when save succeeded; otherwise false.</returns>
    public override async Task<bool> SaveAsync()
    {
        // Allow creating a new entry (EntryId == Guid.Empty). Only invalid when DraftId is missing.
        if (DraftId == Guid.Empty) return false;
        // For editing existing entries ensure we have an Entry and EntryId
        if (EntryId != Guid.Empty && Entry == null) return false;
        if (!HasPendingChanges) return true;

        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            // If this is a new entry creation flow (EntryId was empty originally), create via API
            var isNewEntry = false;
            try
            {
                // detect creation mode: when Entry was null at start and EntryId was Guid.Empty
                if (_entryDetail == null && EntryId == Guid.Empty)
                {
                    isNewEntry = true;
                }
            }
            catch { }

            if (isNewEntry)
            {
                // Build creation payload from pending fields
                var newBookingDate = DateTime.Now.Date;
                DateTime? newValuta = null;
                decimal newAmount = 0m;
                string newSubject = string.Empty;

                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Date", out var pd))
                {
                    if (pd is DateTime dt) newBookingDate = dt;
                    else if (pd is string sdt && DateTime.TryParse(sdt, out var pdt)) newBookingDate = pdt;
                }
                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Valuta", out var pv))
                {
                    if (pv is DateTime vdt) newValuta = vdt;
                    else if (pv is string sv && DateTime.TryParse(sv, out var pvv)) newValuta = pvv;
                }
                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Amount", out var pa))
                {
                    if (pa is decimal d) newAmount = d;
                    else if (pa is string sa && decimal.TryParse(sa, out var pdv)) newAmount = pdv;
                }
                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Subject", out var psub))
                {
                    if (psub is string ss) newSubject = ss;
                    else newSubject = psub?.ToString() ?? string.Empty;
                }

                // capture recipient and booking description from pending fields (if any)
                string? newRecipient = null;
                string? newBookingDesc = null;
                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_Recipient", out var prec))
                {
                    if (prec is string rs) newRecipient = rs;
                    else newRecipient = prec?.ToString();
                }
                if (PendingFieldValues.TryGetValue("Card_Caption_StatementDrafts_BookingDescription", out var pbd))
                {
                    if (pbd is string bds) newBookingDesc = bds;
                    else newBookingDesc = pbd?.ToString();
                }

                var req = new FinanceManager.Shared.Dtos.Statements.StatementDraftAddEntryRequest(newBookingDate, newAmount, newSubject);
                var created = await ApiClient.StatementDrafts_AddEntryAsync(DraftId, req, CancellationToken.None);
                if (created == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Create failed");
                    return false;
                }

                // created likely contains info about the created entry; try to find entry id
                Guid? createdEntryId = null;
                try
                {
                    if (created is FinanceManager.Shared.Dtos.Statements.StatementDraftDetailDto sd)
                    {
                        // Try to find the created entry by matching core properties (best-effort)
                        var match = sd.Entries?.FirstOrDefault(e => e.BookingDate.Date == newBookingDate.Date && e.Amount == newAmount && string.Equals(e.Subject, newSubject, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            createdEntryId = match.Id; // StatementDraftEntryDto.Id
                        }
                    }
                }
                catch { }

                // If we found the created entry id, persist additional core fields (valuta, recipient, bookingDesc) if provided
                if (createdEntryId.HasValue)
                {
                    try
                    {
                        var needUpdateCore = newValuta.HasValue || !string.IsNullOrWhiteSpace(newRecipient) || !string.IsNullOrWhiteSpace(newBookingDesc);
                        if (needUpdateCore)
                        {
                            // currency not available for newly created entry -> pass empty string and let server default
                            var coreReq = new StatementDraftUpdateEntryCoreRequest(newBookingDate, newValuta, newAmount, newSubject, newRecipient, string.Empty, newBookingDesc);
                            var updated = await ApiClient.StatementDrafts_UpdateEntryCoreAsync(DraftId, createdEntryId.Value, coreReq, CancellationToken.None);
                            if (updated != null)
                            {
                                _ = await ApiClient.StatementDrafts_ClassifyEntryAsync(DraftId, createdEntryId.Value, CancellationToken.None);

                                // update succeeded, navigate to created entry using Saved event with proper URL
                                var entryUrl = $"{createdEntryId.Value}?draftId={DraftId}";
                                RaiseUiActionRequested("Saved", entryUrl);
                                return true;
                            }
                            else
                            {
                                // If update failed, surface API error and navigate to draft
                                if (!string.IsNullOrWhiteSpace(ApiClient.LastError)) SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                                var draftUrl = $"/card/statement-drafts/{DraftId}";
                                RaiseUiActionRequested("Saved", draftUrl);
                                return true;
                            }
                        }
                    }
                    catch { /* best-effort, ignore update failures */ }

                    // No extra core fields to update -> navigate to created entry using Saved event and correct URL
                    var entryUrl2 = $"{createdEntryId.Value}?draftId={DraftId}";
                    RaiseUiActionRequested("Saved", entryUrl2);
                    return true;
                }

                // Fallbacks: if created contains DraftId only, navigate back to draft card
                // Navigate back to parent draft card if we don't have entry id
                var fallbackUrl = $"/card/statement-drafts/{DraftId}";
                RaiseUiActionRequested("Saved", fallbackUrl);
                return true;
            }

            // Existing-entry update flow from here on. The guard clause at the top of this
            // method only rejects EntryId != Guid.Empty combined with a null Entry; it cannot
            // guarantee Entry is still non-null here (Entry is a property, not a local, so the
            // compiler cannot track it across the branching above). Re-check defensively.
            if (Entry == null)
            {
                return false;
            }

            // Use the field label keys used when building CardField instances
            const string kDate = "Card_Caption_StatementDrafts_Date";
            const string kValuta = "Card_Caption_StatementDrafts_Valuta";
            const string kAmount = "Card_Caption_StatementDrafts_Amount";
            const string kSubject = "Card_Caption_StatementDrafts_Subject";
            const string kRecipient = "Card_Caption_StatementDrafts_Recipient";
            const string kBookingDesc = "Card_Caption_StatementDrafts_BookingDescription";
            const string kCurrency = "Card_Caption_StatementDrafts_Currency"; // optional

            // Advanced keys
            const string kContact = "Card_Caption_StatementDrafts_Contact";
            const string kCostNeutral = "Card_Caption_StatementDrafts_CostNeutral";
            const string kSavingsPlan = "Card_Caption_StatementDrafts_SavingsPlan";
            const string kArchiveOnBooking = "Card_Caption_StatementDrafts_ArchiveOnBooking";
            const string kSecurity = "Card_Caption_StatementDrafts_Security";
            const string kTransactionType = "Card_Caption_StatementDrafts_TransactionType";
            const string kQuantity = "Card_Caption_StatementDrafts_Quantity";
            const string kFee = "Card_Caption_StatementDrafts_Fee";
            const string kTax = "Card_Caption_StatementDrafts_Tax";

            // Build request from pending values with sensible fallbacks to current DTO
            var bookingDate = Entry.BookingDate;
            var valuta = Entry.ValutaDate;
            var amount = Entry.Amount;
            var subject = Entry.Subject;
            var recipient = Entry.RecipientName;
            var currency = Entry.CurrencyCode;
            var bookingDesc = Entry.BookingDescription;

            // Helper local funcs for parsing
            static DateTime? ParseDate(object? v)
            {
                if (v == null) return null;
                if (v is DateTime dt) return dt;
                if (v is string s && DateTime.TryParse(s, out var parsed)) return parsed;
                return null;
            }
            static decimal? ParseDecimal(object? v)
            {
                if (v == null) return null;
                if (v is decimal d) return d;
                if (v is double db) return (decimal)db;
                if (v is float f) return (decimal)f;
                if (v is string s && decimal.TryParse(s, out var parsed)) return parsed;
                return null;
            }

            var coreProvided = PendingFieldValues.ContainsKey(kDate) || PendingFieldValues.ContainsKey(kValuta) || PendingFieldValues.ContainsKey(kAmount) || PendingFieldValues.ContainsKey(kSubject) || PendingFieldValues.ContainsKey(kRecipient) || PendingFieldValues.ContainsKey(kBookingDesc) || PendingFieldValues.ContainsKey(kCurrency);

            if (coreProvided)
            {
                if (PendingFieldValues.TryGetValue(kDate, out var pd))
                {
                    var pdv = ParseDate(pd);
                    if (pdv.HasValue) bookingDate = pdv.Value;
                }
                if (PendingFieldValues.TryGetValue(kValuta, out var pv))
                {
                    var pvv = ParseDate(pv);
                    if (pvv.HasValue) valuta = pvv.Value;
                }
                if (PendingFieldValues.TryGetValue(kAmount, out var pa))
                {
                    var pav = ParseDecimal(pa);
                    if (pav.HasValue) amount = pav.Value;
                }
                if (PendingFieldValues.TryGetValue(kSubject, out var psb) && psb is string ss) subject = ss;
                if (PendingFieldValues.TryGetValue(kRecipient, out var pr) && pr is string rr) recipient = rr;
                if (PendingFieldValues.TryGetValue(kCurrency, out var pcurr) && pcurr is string cur) currency = cur;
                if (PendingFieldValues.TryGetValue(kBookingDesc, out var pbd) && pbd is string bds) bookingDesc = bds;
            }

            // Only update core booking fields when in edit mode AND the user provided core changes
            if (_isEditMode && coreProvided)
            {
                var reqCore = new StatementDraftUpdateEntryCoreRequest(bookingDate, valuta, amount, subject, recipient, currency, bookingDesc);
                var updated = await ApiClient.StatementDrafts_UpdateEntryCoreAsync(DraftId, EntryId, reqCore, CancellationToken.None);
                if (updated == null)
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError ?? "Save failed");
                    return false;
                }
                Entry = updated;
            }

            // Persist other editable fields in one atomic request where possible
            Guid? contactId = null;
            bool? isCostNeutral = null;
            Guid? savingsPlanId = null;
            bool? archiveOnBooking = null;
            Guid? securityId = null;
            SecurityTransactionType? txType = null;
            decimal? quantity = null;
            decimal? fee = null;
            decimal? tax = null;

            // track whether user explicitly provided savings plan lookup (for clear vs preserve)
            var savingsPlanProvided = PendingFieldValues.ContainsKey(kSavingsPlan);
            // separate: was the security lookup explicitly provided, and were other security-related fields changed?
            var securityLookupProvided = PendingFieldValues.ContainsKey(kSecurity);
            var securityOtherProvided = PendingFieldValues.ContainsKey(kTransactionType) || PendingFieldValues.ContainsKey(kQuantity) || PendingFieldValues.ContainsKey(kFee) || PendingFieldValues.ContainsKey(kTax);
            // overall we consider security-related changes present when either lookup or other security fields were changed
            var securityProvided = securityLookupProvided || securityOtherProvided;

            if (PendingFieldValues.TryGetValue(kContact, out var pc))
            {
                switch (pc)
                {
                    case LookupItem li: contactId = li.Key; break;
                    case Guid g: contactId = g; break;
                    case string s when Guid.TryParse(s, out var gg): contactId = gg; break;
                }
            }

            if (PendingFieldValues.TryGetValue(kCostNeutral, out var pcn))
            {
                switch (pcn)
                {
                    case bool b: isCostNeutral = b; break;
                    case string s when bool.TryParse(s, out var bb): isCostNeutral = bb; break;
                }
            }

            if (PendingFieldValues.TryGetValue(kSavingsPlan, out var ps))
            {
                switch (ps)
                {
                    case LookupItem li: savingsPlanId = li.Key; break;
                    case Guid g: savingsPlanId = g; break;
                    case string s when Guid.TryParse(s, out var gg): savingsPlanId = gg; break;
                    case string s when string.IsNullOrWhiteSpace(s): savingsPlanId = null; break;
                }
            }

            if (PendingFieldValues.TryGetValue(kArchiveOnBooking, out var pab))
            {
                switch (pab)
                {
                    case bool b: archiveOnBooking = b; break;
                    case string s when bool.TryParse(s, out var bb): archiveOnBooking = bb; break;
                }
            }

            if (PendingFieldValues.TryGetValue(kSecurity, out var psx))
            {
                switch (psx)
                {
                    case LookupItem li: securityId = li.Key; break;
                    case Guid g: securityId = g; break;
                    case string s when Guid.TryParse(s, out var gg): securityId = gg; break;
                    case string s when string.IsNullOrWhiteSpace(s): securityId = Guid.Empty; break; // explicit clear
                }
            }

            // Transaction type: support LookupItem (may contain localized display) -> map to enum
            if (PendingFieldValues.TryGetValue(kTransactionType, out var ptt))
            {
                // Try direct enum
                if (ptt is SecurityTransactionType st) { txType = st; }
                else
                {
                    string? candidate = null;
                    // If it's a LookupItem-like object, try to read Name property
                    try
                    {
                        if (ptt != null)
                        {
                            var t = ptt.GetType();
                            var nameProp = t.GetProperty("Name");
                            if (nameProp != null)
                            {
                                candidate = nameProp.GetValue(ptt)?.ToString();
                            }
                        }
                    }
                    catch { /* ignore */ }

                    // Fallback to string conversion
                    if (string.IsNullOrWhiteSpace(candidate)) candidate = ptt?.ToString();

                    if (string.IsNullOrWhiteSpace(candidate))
                    {
                        txType = null; // explicit clear
                    }
                    else
                    {
                        // Try parse raw enum name
                        if (Enum.TryParse<SecurityTransactionType>(candidate, true, out var parsed2))
                        {
                            txType = parsed2;
                        }
                        else
                        {
                            // Try localized mapping via resources
                            try
                            {
                                var enumType = typeof(SecurityTransactionType);
                                var localizer = ServiceProvider.GetService<IStringLocalizer<Pages>>();
                                foreach (var name in Enum.GetNames(enumType))
                                {
                                    var key = $"EnumType_{enumType.Name}_{name}";
                                    var localized = localizer?[key]?.Value;
                                    if (!string.IsNullOrWhiteSpace(localized) && string.Equals(localized, candidate, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (Enum.TryParse<SecurityTransactionType>(name, true, out var parsed)) { txType = parsed; break; }
                                    }
                                }
                            }
                            catch { /* ignore mapping failures */ }
                        }
                    }
                }
            }

            if (PendingFieldValues.TryGetValue(kQuantity, out var pq))
            {
                // explicit empty string -> clear
                if (pq is string qs && string.IsNullOrWhiteSpace(qs)) { quantity = null; }
                else
                {
                    var qv = ParseDecimal(pq);
                    if (qv.HasValue) quantity = qv.Value;
                }
            }
            if (PendingFieldValues.TryGetValue(kFee, out var pf))
            {
                if (pf is string fs && string.IsNullOrWhiteSpace(fs)) { fee = null; }
                else
                {
                    var fv = ParseDecimal(pf);
                    if (fv.HasValue) fee = fv.Value;
                }
            }
            if (PendingFieldValues.TryGetValue(kTax, out var ptv))
            {
                if (ptv is string ts && string.IsNullOrWhiteSpace(ts)) { tax = null; }
                else
                {
                    var tv = ParseDecimal(ptv);
                    if (tv.HasValue) tax = tv.Value;
                }
            }

            // Preserve existing values for fields that were not changed by the user.
            // Otherwise sending null will clear them on the server.
            if (Entry != null)
            {
                if (contactId == null) contactId = Entry.ContactId;
                if (isCostNeutral == null) isCostNeutral = Entry.IsCostNeutral;
                // Only preserve savingsPlan when the user did NOT provide the field.
                if (!savingsPlanProvided)
                {
                    if (savingsPlanId == null) savingsPlanId = Entry.SavingsPlanId;
                }
                // If user provided Guid.Empty (from lookup clear), treat as explicit clear
                if (savingsPlanId == Guid.Empty) savingsPlanId = null;
                if (archiveOnBooking == null) archiveOnBooking = Entry.ArchiveSavingsPlanOnBooking;
                // Security: if the user did NOT change the security lookup itself, preserve the existing security id
                if (!securityLookupProvided)
                {
                    if (securityId == null) securityId = Entry.SecurityId;
                }
                // For other security-related fields preserve values when user didn't change them
                if (txType == null) txType = Entry.SecurityTransactionType;
                if (quantity == null) quantity = Entry.SecurityQuantity;
                if (fee == null) fee = Entry.SecurityFeeAmount;
                if (tax == null) tax = Entry.SecurityTaxAmount;
                // If user explicitly cleared the lookup (Guid.Empty), treat as explicit clear
                if (securityLookupProvided && securityId == Guid.Empty) securityId = null;
            }

            // If any of the advanced fields are present OR the user explicitly provided savings/security (even to clear), call SaveEntryAll
            if (contactId != null || isCostNeutral != null || savingsPlanId != null || securityId != null || txType != null || quantity != null || fee != null || tax != null || archiveOnBooking != null || savingsPlanProvided || securityProvided)
            {
                var allReq = new StatementDraftSaveEntryAllRequest(contactId, isCostNeutral, savingsPlanId, archiveOnBooking, securityId, txType, quantity, fee, tax);
                try
                {
                    await ApiClient.StatementDrafts_SaveEntryAllAsync(DraftId, EntryId, allReq, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrWhiteSpace(ApiClient.LastError))
                    {
                        SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                        return false;
                    }

                    // Surface unexpected exceptions to the UI
                    SetError(null, ex.Message);
                    return false;
                }

                // If API client reported a domain/validation error, show it
                if (!string.IsNullOrWhiteSpace(ApiClient.LastError))
                {
                    SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                    return false;
                }

                // Refresh entry to pick up all persisted changes
                var refreshed = await ApiClient.StatementDrafts_GetEntryAsync(DraftId, EntryId, CancellationToken.None);
                if (refreshed != null)
                {
                    _entryDetail = refreshed;
                    Entry = refreshed.Entry;
                }
            }

            // If we did NOT call the advanced save endpoint, we may still have updated core fields above.
            // Ensure we have the latest Entry state by fetching if necessary.
            if (Entry == null)
            {
                var refreshed2 = await ApiClient.StatementDrafts_GetEntryAsync(DraftId, EntryId, CancellationToken.None);
                if (refreshed2 != null)
                {
                    _entryDetail = refreshed2;
                    Entry = refreshed2.Entry;
                }
            }

            // Update cached lookup display names so UI rebuild uses fresh values
            try
            {
                _contactName = string.Empty; _savingsPlanName = string.Empty; _securityName = string.Empty; _contactIsSelf = false;
                if (Entry?.ContactId.HasValue == true)
                {
                    var c = await ApiClient.Contacts_GetAsync(Entry.ContactId.Value, CancellationToken.None);
                    _contactName = c?.Name ?? string.Empty;
                    _contactIsSelf = c?.Type == ContactType.Self;
                }
                try
                {
                    var draftHeader = await ApiClient.StatementDrafts_GetAsync(DraftId, headerOnly: true, src: null, fromEntryDraftId: null, fromEntryId: null, CancellationToken.None);
                    if (draftHeader?.DetectedAccountId is Guid acctId)
                    {
                        var detectedAccount = await ApiClient.GetAccountAsync(acctId, CancellationToken.None);
                        if (detectedAccount != null)
                        {
                            _accountAllowsSavings = detectedAccount.SavingsPlanExpectation != SavingsPlanExpectation.None;
                            _accountAllowsSecurity = detectedAccount.SecurityProcessingEnabled;
                        }
                    }
                }
                catch { /* ignore */ }
                if (Entry?.SavingsPlanId.HasValue == true)
                {
                    var sp = await ApiClient.SavingsPlans_GetAsync(Entry.SavingsPlanId.Value, CancellationToken.None);
                    _savingsPlanName = sp?.Name ?? string.Empty;
                }
                if (Entry?.SecurityId.HasValue == true)
                {
                    var sx = await ApiClient.Securities_GetAsync(Entry.SecurityId.Value, CancellationToken.None);
                    _securityName = sx?.Name ?? string.Empty;
                }
            }
            catch { /* ignore lookup refresh failures */ }

            // Clear pending changes after persistence
            ClearPendingChanges();

            // refresh card
            CardRecord = new CardRecord(BuildFields(), Entry);

            // If BudgetImpact is present after save, show a budget impact hint panel
            if (Entry?.BudgetImpact != null && Entry.BudgetImpact.Hints != null && Entry.BudgetImpact.Hints.Count > 0)
            {
                try
                {
                    var parameters = new Dictionary<string, object?> { ["BudgetImpact"] = Entry.BudgetImpact };
                    var spec = new BaseViewModel.EmbeddedPanelSpec(typeof(FinanceManager.Web.Components.Statements.BudgetImpactSummaryPanel), parameters, EmbeddedPanelPosition.AfterRibbon, true);
                    RaiseUiEmbeddedPanelRequested(spec);
                }
                catch { /* best-effort hint display */ }
            }

            return true;
        }
        catch (Exception ex)
        {
            SetError(ApiClient.LastErrorCode, ApiClient.LastError ?? ex.Message);
            return false;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Validates the current entry via the API and returns validation results.
    /// </summary>
    /// <returns>A <see cref="DraftValidationResultDto"/> when validation could be performed; otherwise <c>null</c>.</returns>
    public async Task<DraftValidationResultDto?> ValidateAsync()
    {
        if (EntryId == Guid.Empty || DraftId == Guid.Empty) return null;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var res = await ApiClient.StatementDrafts_ValidateEntryAsync(DraftId, EntryId, CancellationToken.None);
            return res;
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            return null;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Attempts to book the current entry by calling the API. When booking succeeds the view model will try to navigate
    /// to the next open entry or previous entry; otherwise it returns the booking result for further handling by the caller.
    /// </summary>
    /// <param name="forceWarnings">When true force booking even if warnings are present.</param>
    /// <returns>A <see cref="BookingResult"/> when booking could be attempted; otherwise <c>null</c>.</returns>
    public async Task<BookingResult?> BookEntryAsync(bool forceWarnings = false)
    {
        if (EntryId == Guid.Empty || DraftId == Guid.Empty) return null;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var res = await ApiClient.StatementDrafts_BookEntryAsync(DraftId, EntryId, forceWarnings, CancellationToken.None);

            // If booking succeeded (and wasn't withheld due to warnings), navigate to next/previous entry.
            // Use neighbor information from the currently loaded entry detail (_entryDetail) which was populated when the entry was loaded.
            if (res != null && res.Success && !res.HasWarnings)
            {
                try
                {
                    // Prefer next open entry if present, then next entry, otherwise fall back to previous entry.
                    var nextId = _entryDetail?.NextOpenEntryId ?? _entryDetail?.NextEntryId;
                    if (nextId.HasValue && nextId != Guid.Empty)
                    {
                        // Load next entry
                        await LoadAsync(nextId.Value);
                    }
                    else if (_entryDetail?.PrevEntryId.HasValue == true && _entryDetail.PrevEntryId != Guid.Empty)
                    {
                        // No next -> load previous (now last in list)
                        await LoadAsync(_entryDetail.PrevEntryId.Value);
                    }
                    else
                    {
                        // No neighbors: navigate back to parent draft card
                        RaiseUiActionRequested("Back", $"card,{DraftId}");
                    }
                }
                catch
                {
                    // Ignore navigation/load failures; return result to caller
                }
            }

            return res;
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            return null;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }

    /// <summary>
    /// Builds ribbon register definitions for the entry card. This includes navigation, actions and linked information tabs.
    /// </summary>
    /// <param name="localizer">Localizer used to resolve UI labels.</param>
    /// <returns>Collection of ribbon registers or <c>null</c> when none are provided.</returns>
    protected override IReadOnlyList<UiRibbonRegister>? GetRibbonRegisterDefinition(IStringLocalizer localizer)
    {
        var tabs = new List<UiRibbonTab>();

        var navItems = new List<UiRibbonAction>
        {
            // include DraftId as payload so pages can navigate back to this card with context
            new UiRibbonAction("Back", localizer["Ribbon_Back"].Value, "<svg><use href='/icons/sprite.svg#back'/></svg>", UiRibbonItemSize.Large, Entry == null, null, new Func<Task>(() => OnBackRequestedAsync())) { MobileShortcut = true },
            new UiRibbonAction("Prev", localizer["Ribbon_Prev"].Value, "<svg><use href='/icons/sprite.svg#chevron-left'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(OnPrevRequestedAsync)) { MobileShortcut = true },
            new UiRibbonAction("Next", localizer["Ribbon_Next"].Value, "<svg><use href='/icons/sprite.svg#chevron-right'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(OnNextRequestedAsync)) { MobileShortcut = true }
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Navigation"].Value, navItems));

        var actions = new List<UiRibbonAction>
        {
            new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Small, (Entry == null && EntryId != Guid.Empty), null, new Func<Task>(async () => { await SaveAsync(); })) { MobileShortcut = true },
            new UiRibbonAction("Validate", localizer["Ribbon_Validate"].Value, "<svg><use href='/icons/sprite.svg#check'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(async () => { await ValidateAsync(); })),
            new UiRibbonAction("BookEntry", localizer["Ribbon_Book"].Value, "<svg><use href='/icons/sprite.svg#postings'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(async () => { await BookEntryAsync(false); }))
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Actions"].Value, actions));

        // Linked information group: open assigned Contact, SavingsPlan, Security (disabled when not assigned)
        var linkedActions = new List<UiRibbonAction>
        {
            new UiRibbonAction("OpenContact", localizer["Ribbon_OpenContact"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Entry == null || Entry.ContactId == null, null, new Func<Task>(() => {
                if (Entry?.ContactId != null)
                {
                    try
                    {
                        var nav = ServiceProvider.GetService<NavigationManager>();
                        var back = nav?.Uri ?? string.Empty;
                        var url = $"/card/contacts/{Entry.ContactId}";
                        if (!string.IsNullOrEmpty(back)) url += "?back=" + System.Uri.EscapeDataString(back);
                        RaiseUiActionRequested("OpenPostings", url);
                    }
                    catch
                    {
                        var url = $"/card/contacts/{Entry.ContactId}";
                        RaiseUiActionRequested("OpenPostings", url);
                    }
                }
                return Task.CompletedTask;
            })) { MobileShortcut = true },
            new UiRibbonAction("OpenSavingsPlan", localizer["Ribbon_OpenSavingsPlan"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Entry == null || Entry.SavingsPlanId == null, null, new Func<Task>(() => {
                if (Entry?.SavingsPlanId != null)
                {
                    try
                    {
                        var nav = ServiceProvider.GetService<NavigationManager>();
                        var back = nav?.Uri ?? string.Empty;
                        var url = $"/card/savings-plans/{Entry.SavingsPlanId}";
                        if (!string.IsNullOrEmpty(back)) url += "?back=" + System.Uri.EscapeDataString(back);
                        RaiseUiActionRequested("OpenPostings", url);
                    }
                    catch
                    {
                        var url = $"/card/savings-plans/{Entry.SavingsPlanId}";
                        RaiseUiActionRequested("OpenPostings", url);
                    }
                }
                return Task.CompletedTask;
            })) { MobileShortcut = true },
            new UiRibbonAction("OpenSecurity", localizer["Ribbon_OpenSecurity"].Value, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, Entry == null || Entry.SecurityId == null, null, new Func<Task>(() => {
                 if (Entry?.SecurityId != null)
                 {
                     try
                     {
                         var nav = ServiceProvider.GetService<NavigationManager>();
                         var back = nav?.Uri ?? string.Empty;
                         var url = $"/card/securities/{Entry.SecurityId}";
                         if (!string.IsNullOrEmpty(back)) url += "?back=" + System.Uri.EscapeDataString(back);
                         RaiseUiActionRequested("OpenPostings", url);
                     }
                     catch
                     {
                         var url = $"/card/securities/{Entry.SecurityId}";
                         RaiseUiActionRequested("OpenPostings", url);
                     }
                 }
                 return Task.CompletedTask;
             })) { MobileShortcut = true }
        };
        tabs.Add(new UiRibbonTab(localizer["Ribbon_Group_Linked"].Value, linkedActions));

        // If the entry's contact equals the bank contact for the draft, allow assigning/opening a linked statement
        try
        {
            // Offer assign/open statement actions when the assigned contact on the entry is a payment intermediary.
            if (_contactIsPaymentIntermediary)
            {
                // If there is already a SplitDraft assigned, allow opening it
                if (Entry?.SplitDraftId != null && Entry.SplitDraftId != Guid.Empty)
                {
                    var openLabel = localizer["Ribbon_OpenAssignedStatement"].Value;
                    linkedActions.Add(new UiRibbonAction("OpenAssignedStatement", openLabel, "<svg><use href='/icons/sprite.svg#external'/></svg>", UiRibbonItemSize.Small, false, null, () =>
                    {
                        var url = $"/card/statement-drafts/{Entry.SplitDraftId}";
                        RaiseUiActionRequested("OpenPostings", url);
                        return Task.CompletedTask;
                    }));
                    // Allow unassigning the association
                    var unassignLabel = localizer["Ribbon_UnassignStatement"].Value;
                    actions.Add(new UiRibbonAction("UnassignStatement", unassignLabel, "<svg><use href='/icons/sprite.svg#unlink'/></svg>", UiRibbonItemSize.Small, false, null, async () => { await UnassignStatementAsync(); }));
                }
                else
                {
                    var assignLabel = localizer["Ribbon_AssignStatement"].Value;
                    actions.Add(new UiRibbonAction("AssignStatement", assignLabel, "<svg><use href='/icons/sprite.svg#link'/></svg>", UiRibbonItemSize.Small, false, null, async () =>
                    {
                        try
                        {
                            // Load open drafts and filter to those without detected account assignment and not the current draft
                            var drafts = await ApiClient.StatementDrafts_ListOpenAsync(skip: 0, take: 200, CancellationToken.None);
                            var candidates = drafts?
                                .Where(d => d.DraftId != DraftId && d.DetectedAccountId == null)
                                .Select(d => new FinanceManager.Web.ViewModels.Common.BaseViewModel.LookupItem(d.DraftId, d.OriginalFileName))
                                .ToList() ?? new List<FinanceManager.Web.ViewModels.Common.BaseViewModel.LookupItem>();

                            var specParams = new Dictionary<string, object?>
                            {
                                ["DraftId"] = DraftId,
                                ["EntryId"] = EntryId,
                                ["BankContactId"] = _entryDetail?.BankContactId,
                                ["Candidates"] = candidates,
                                ["Title"] = localizer["AssignStatement_Title"].Value
                            };
                            var spec = new FinanceManager.Web.ViewModels.Common.BaseViewModel.UiOverlaySpec(typeof(FinanceManager.Web.Components.Shared.AssignStatementOverlay), specParams);
                            RaiseUiActionRequested("AssignStatement", spec);
                        }
                        catch
                        {
                            // ignore failures to load candidates - still open overlay with empty list
                            var specParams = new Dictionary<string, object?>
                            {
                                ["DraftId"] = DraftId,
                                ["EntryId"] = EntryId,
                                ["BankContactId"] = _entryDetail?.BankContactId,
                                ["Candidates"] = new List<FinanceManager.Web.ViewModels.Common.BaseViewModel.LookupItem>(),
                                ["Title"] = localizer["AssignStatement_Title"].Value
                            };
                            var spec = new FinanceManager.Web.ViewModels.Common.BaseViewModel.UiOverlaySpec(typeof(FinanceManager.Web.Components.Shared.AssignStatementOverlay), specParams);
                            RaiseUiActionRequested("AssignStatement", spec);
                        }
                        return;
                    }));
                }
            }
        }
        catch { }

        // Edit/Read-only toggle or Reset for AlreadyBooked
        if (Entry != null && Entry.Status == StatementDraftEntryStatus.AlreadyBooked)
        {
            var resetLabel = localizer["Ribbon_ResetDuplicate"].Value;
            var resetAction = new UiRibbonAction("ResetDuplicate", resetLabel, "<svg><use href='/icons/sprite.svg#reset'/></svg>", UiRibbonItemSize.Small, false, null, new Func<Task>(async () => { await ResetDuplicateAsync(); }));
            actions.Insert(0, resetAction);
            // disable Save while entry is locked
            actions.RemoveAll(a => a.Id == "Save");
            actions.Insert(0, new UiRibbonAction("Save", localizer["Ribbon_Save"].Value, "<svg><use href='/icons/sprite.svg#save'/></svg>", UiRibbonItemSize.Small, true, null, new Func<Task>(async () => { await SaveAsync(); }))); // disabled
            // Delete action (available even when locked)
            var delLabelLocked = localizer["Ribbon_Delete"].Value;
            var delActionLocked = new UiRibbonAction("Delete", delLabelLocked, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, false, null, new Func<Task>(async () => { await DeleteEntryAsync(); }));
            actions.Insert(0, delActionLocked);
        }
        else
        {
            var editAction = new UiRibbonAction("Edit", localizer[_isEditMode ? "Ribbon_ReadOnly" : "Ribbon_Edit"].Value, "<svg><use href='/icons/sprite.svg#edit'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(() => ToggleEditModeAsync())) { MobileShortcut = true };
            actions.Insert(0, editAction);
            // Delete action
            var delLabel = localizer["Ribbon_Delete"].Value;
            var delAction = new UiRibbonAction("Delete", delLabel, "<svg><use href='/icons/sprite.svg#delete'/></svg>", UiRibbonItemSize.Small, Entry == null, null, new Func<Task>(async () => { await DeleteEntryAsync(); }));
            actions.Insert(0, delAction);
        }

        var registers = new List<UiRibbonRegister> { new UiRibbonRegister(UiRibbonRegisterKind.Actions, tabs) };
        var baseRegs = base.GetRibbonRegisterDefinition(localizer);
        if (baseRegs != null) registers.AddRange(baseRegs);
        return registers.Count == 0 ? null : registers;
    }

    /// <summary>
    /// Indicates whether symbol upload is allowed for this entry card.
    /// </summary>
    /// <returns><c>true</c> when symbol uploads are permitted for statement draft entries.</returns>
    protected override bool IsSymbolUploadAllowed() => true;

    /// <summary>
    /// Returns the symbol attachment parent kind and id for this entry card.
    /// </summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="EntryId">The entry id.</param>
    /// <returns>Tuple containing the <see cref="Domain.Attachments.AttachmentEntityKind"/> and the parent id to use for attachments.</returns>
    protected override (Domain.Attachments.AttachmentEntityKind Kind, Guid ParentId) GetSymbolParent() => (Domain.Attachments.AttachmentEntityKind.StatementDraftEntry, EntryId);

    /// <summary>
    /// Assigns a newly uploaded symbol attachment to this entry. The implementation reloads the entry to pick up server-side changes.
    /// Exceptions are swallowed to avoid interrupting UI flows.
    /// </summary>
    /// <param name="attachmentId">Attachment id to assign, or <c>null</c> to clear the symbol.</param>
    /// <returns>A task that completes when the operation has finished.</returns>
    protected override async Task AssignNewSymbolAsync(Guid? attachmentId)
    {
        try
        {
            // For statement draft entries the attachment is uploaded with the correct parent id
            // so we only need to reload the entry to pick up any server-side changes.
            if (EntryId != Guid.Empty)
            {
                await LoadAsync(EntryId);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Handles the ribbon's Back action by requesting a UI navigation payload containing the parent draft id.
    /// </summary>
    /// <returns>A completed task after the request has been issued.</returns>
    private Task OnBackRequestedAsync()
    {
        try
        {
            var logger = ServiceProvider.GetService<ILogger<StatementDraftEntryCardViewModel>>();
            logger?.LogInformation("Ribbon Back pressed on StatementDraftEntry (EntryId={EntryId}, DraftId={DraftId})", EntryId, DraftId);
        }
        catch { }

        // Provide payload following the comma-separated convention: "card,{draftId}"
        RaiseUiActionRequested("Back", $"card,{DraftId}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles navigation to the previous entry when available; otherwise requests navigation back to the parent draft.
    /// </summary>
    /// <returns>A task that completes when navigation has been processed.</returns>
    private async Task OnPrevRequestedAsync()
    {
        try
        {
            if (_entryDetail == null)
            {
                return;
            }
            var prevId = _entryDetail.PrevEntryId;
            if (prevId.HasValue && prevId != Guid.Empty)
            {
                await LoadAsync(prevId.Value);
                return;
            }
            // No previous -> navigate back to parent draft card
            if (DraftId != Guid.Empty)
            {
                RaiseUiActionRequested("Back", $"card,{DraftId}");
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Handles navigation to the next entry (preferring next-open) or wraps to previous if none available.
    /// </summary>
    /// <returns>A task that completes when navigation has been processed.</returns>
    private async Task OnNextRequestedAsync()
    {
        try
        {
            if (_entryDetail == null)
            {
                return;
            }
            var nextId = _entryDetail.NextOpenEntryId ?? _entryDetail.NextEntryId;
            if (nextId.HasValue && nextId != Guid.Empty)
            {
                await LoadAsync(nextId.Value);
                return;
            }
            // If there's no next, but there is a previous, navigate to previous (wrap-around behavior)
            if (_entryDetail.PrevEntryId.HasValue && _entryDetail.PrevEntryId != Guid.Empty)
            {
                await LoadAsync(_entryDetail.PrevEntryId.Value);
                return;
            }
            // No neighbors: navigate back to parent draft card
            if (DraftId != Guid.Empty)
            {
                RaiseUiActionRequested("Back", $"card,{DraftId}");
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Resets the duplicate/already-booked state for the entry via the API and refreshes the card.
    /// </summary>
    /// <returns>A task that completes when the reset operation has finished.</returns>
    private async Task ResetDuplicateAsync()
    {
        if (Entry == null || EntryId == Guid.Empty || DraftId == Guid.Empty) return;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            // Call API to reset duplicate entry status
            await ApiClient.StatementDrafts_ResetDuplicateEntryAsync(DraftId, EntryId, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(ApiClient.LastError))
            {
                SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                return;
            }
            // Refresh entry
            var refreshed = await ApiClient.StatementDrafts_GetEntryAsync(DraftId, EntryId, CancellationToken.None);
            if (refreshed != null)
            {
                _entryDetail = refreshed;
                Entry = refreshed.Entry;
            }
            // After reset editing becomes available according to rules
            _isEditMode = false;
            CardRecord = new CardRecord(BuildFields(), Entry);
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
    /// Unassigns an associated split draft from this entry.
    /// </summary>
    /// <returns>A task that completes when the unassign operation has finished.</returns>
    private async Task UnassignStatementAsync()
    {
        if (Entry == null || EntryId == Guid.Empty || DraftId == Guid.Empty) return;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            // Clear association by sending null SplitDraftId
            var req = new FinanceManager.Shared.Dtos.Statements.StatementDraftSetSplitDraftRequest(null);
            var updated = await ApiClient.StatementDrafts_SetEntrySplitDraftAsync(DraftId, EntryId, req, CancellationToken.None);
            if (updated == null)
            {
                if (!string.IsNullOrWhiteSpace(ApiClient.LastError)) SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                else SetError(null, "Unassign failed");
                return;
            }

            // refresh local state
            Entry = updated.Entry;
            var refreshed = await ApiClient.StatementDrafts_GetEntryAsync(DraftId, EntryId, CancellationToken.None);
            if (refreshed != null)
            {
                _entryDetail = refreshed;
                Entry = refreshed.Entry;
            }
            CardRecord = new CardRecord(BuildFields(), Entry);
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
    /// Deletes the current entry or, in case of a clone, navigates back to the draft.
    /// </summary>
    /// <returns>True on successful delete, otherwise false.</returns>
    public override async Task<bool> DeleteAsync()
    {
        return await DeleteEntryAsync();
    }

    /// <summary>
    /// Deletes the current entry asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous delete operation. The task result is <c>true</c> when the deletion succeeded; otherwise <c>false</c>.</returns>
    private async Task<bool> DeleteEntryAsync()
    {
        if (Entry == null || EntryId == Guid.Empty || DraftId == Guid.Empty) return false;
        Loading = true; SetError(null, null); RaiseStateChanged();
        try
        {
            var ok = await ApiClient.StatementDrafts_DeleteEntryAsync(DraftId, EntryId, CancellationToken.None);
            if (!ok)
            {
                if (!string.IsNullOrWhiteSpace(ApiClient.LastError)) SetError(ApiClient.LastErrorCode ?? null, ApiClient.LastError);
                else SetError(null, "Delete failed");
                return false;
            }

            // navigate back to parent card (payload convention)
            RaiseUiActionRequested("Back", $"card,{DraftId}");
            return true;
        }
        catch (Exception ex)
        {
            SetError(null, ex.Message);
            return false;
        }
        finally
        {
            Loading = false; RaiseStateChanged();
        }
    }
}
