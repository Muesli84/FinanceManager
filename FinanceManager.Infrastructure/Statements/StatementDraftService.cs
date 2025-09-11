using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Reader;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos; // added

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService : IStatementDraftService // partial now for clarity if split later
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileReader> _statementFileReaders;
    public StatementDraftService(AppDbContext db) { _db = db; _statementFileReaders = new List<IStatementFileReader>() { new ING_StatementFileReader(), new Barclays_StatementFileReader() }; }

    public async Task<StatementDraftDto> CreateDraftAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct)
    {
        var parsedDraft = _statementFileReaders
            .Select(reader => reader.Parse(originalFileName, fileBytes))
            .Where(result => result is not null && result.Movements.Any())
            .FirstOrDefault();

        if (parsedDraft is null)
        {
            throw new InvalidOperationException("No valid statement file reader found or no movements detected.");
        }

        var draft = new StatementDraft(ownerUserId, originalFileName, parsedDraft.Header.AccountNumber);
        draft.SetOriginalFile(fileBytes, null);

        if (!string.IsNullOrWhiteSpace(parsedDraft.Header.IBAN))
        {
            var account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId && a.Iban == parsedDraft.Header.IBAN, ct);
            if (account != null)
            {
                draft.SetDetectedAccount(account.Id);
            }
        }

        foreach (var movement in parsedDraft.Movements)
        {
            draft.AddEntry(movement.BookingDate, movement.Amount, movement.Subject ?? string.Empty, movement.Counterparty, movement.ValutaDate, movement.CurrencyCode, movement.PostingDescription, movement.IsPreview, false);
        }

        _db.StatementDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        // Auto classify after creation
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return Map(draft); // einfache Variante ohne SplitLookup
    }

    public async Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, CancellationToken ct)
    {
        var drafts = await _db.StatementDrafts
            .Include(d => d.Entries)
            .Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft)
            .OrderByDescending(d => d.CreatedUtc)
            .ToListAsync(ct);

        // Lookup: welche Drafts sind als SplitDraft verknüpft
        var splitLinks = await _db.StatementDraftEntries
            .Where(e => e.SplitDraftId != null)
            .Select(e => new { e.Id, e.DraftId, e.SplitDraftId, e.Amount })
            .ToListAsync(ct);

        var bySplitId = splitLinks
            .GroupBy(x => x.SplitDraftId!.Value)
            .ToDictionary(g => g.Key, g => (dynamic)g.First()); // Eintrag pro SplitDraftId

        return drafts.Select(d => Map(d, bySplitId)).ToList();
    }

    public async Task<StatementDraftDto?> GetDraftAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }

        var splitRef = await _db.StatementDraftEntries
            .Where(e => e.SplitDraftId == draft.Id)
            .Select(e => new { e.DraftId, e.Id, e.Amount })
            .FirstOrDefaultAsync(ct);

        if (splitRef == null)
        {
            return Map(draft); // keine Parent-Referenz
        }

        var dict = new Dictionary<Guid, dynamic> { { draft.Id, (dynamic)splitRef } };
        return Map(draft, dict);
    }

    public async Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        _db.Entry(draft.AddEntry(bookingDate, amount, subject)).State = EntityState.Added;
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        // If this draft is used as a split draft for a parent entry -> reevaluate parent status
        if (await _db.StatementDraftEntries.AnyAsync(e => e.SplitDraftId == draft.Id, ct))
        {
            await ReevaluateParentEntryStatusAsync(ownerUserId, draft.Id, ct);
        }
        return await GetDraftAsync(draftId, ownerUserId, ct); // Sicherstellen, dass Mapping mit evtl. SplitRefs erfolgt
    }

    public async Task<CommitResult?> CommitAsync(Guid draftId, Guid ownerUserId, Guid accountId, ImportFormat format, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        if (!await _db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct)) { return null; }

        var import = new StatementImport(accountId, format, draft.OriginalFileName);
        _db.StatementImports.Add(import);
        await _db.SaveChangesAsync(ct);

        foreach (var e in draft.Entries)
        {
            _db.StatementEntries.Add(new StatementEntry(
                import.Id,
                e.BookingDate,
                e.Amount,
                e.Subject,
                Guid.NewGuid().ToString(),
                e.RecipientName,
                e.ValutaDate,
                e.CurrencyCode,
                e.BookingDescription,
                e.IsAnnounced,
                e.IsCostNeutral));
        }
        import.GetType().GetProperty("TotalEntries")!.SetValue(import, draft.Entries.Count);
        draft.MarkCommitted();
        await _db.SaveChangesAsync(ct);

        return new CommitResult(import.Id, draft.Entries.Count);
    }

    public async Task<bool> CancelAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return false; }
        _db.StatementDrafts.Remove(draft);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<StatementDraftDto?> ClassifyAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    public async Task<StatementDraftDto?> SetAccountAsync(Guid draftId, Guid ownerUserId, Guid accountId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        var accountExists = await _db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct);
        if (!accountExists) { return null; }
        draft.SetDetectedAccount(accountId);
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    public void TryAutoAssignSavingsPlan(StatementDraftEntry entry, IEnumerable<SavingsPlan> userPlans, Domain.Contacts.Contact selfContact)
    {
        if (entry.ContactId is null) { return; }
        if (entry.ContactId != selfContact.Id) { return; }

        // Hilfsfunktion zur Normalisierung von Umlauten
        static string NormalizeUmlauts(string text)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            return text
                .Replace("ä", "ae", StringComparison.OrdinalIgnoreCase)
                .Replace("ö", "oe", StringComparison.OrdinalIgnoreCase)
                .Replace("ü", "ue", StringComparison.OrdinalIgnoreCase)
                .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase)
                .Replace(" ", "", StringComparison.OrdinalIgnoreCase);
        }

        var normalizedSubject = NormalizeUmlauts(entry.Subject).ToLowerInvariant();

        foreach (var plan in userPlans)
        {
            if (string.IsNullOrWhiteSpace(plan.Name)) { continue; }
            var normalizedPlanName = NormalizeUmlauts(plan.Name).ToLowerInvariant();
            if (normalizedSubject.Contains(normalizedPlanName))
            {
                entry.AssignSavingsPlan(plan.Id);
                break;
            }
        }
    }

    public async Task<StatementDraftDto> AssignSavingsPlanAsync(Guid draftId, Guid entryId, Guid? savingsPlanId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null!; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null!; }
        entry.AssignSavingsPlan(savingsPlanId);
        await _db.SaveChangesAsync(ct);
        return (await GetDraftAsync(draftId, ownerUserId, ct))!;
    }

    public async Task<StatementDraftDto?> SetEntryContactAsync(Guid draftId, Guid entryId, Guid? contactId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }

        if (contactId == null)
        {
            entry.ClearContact();
        }
        else
        {
            bool contactExists = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == ownerUserId, ct);
            if (!contactExists) { return null; }
            entry.MarkAccounted(contactId.Value);
        }
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    public async Task<StatementDraftDto?> SetEntryCostNeutralAsync(Guid draftId, Guid entryId, bool? isCostNeutral, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }

        entry.MarkCostNeutral(isCostNeutral ?? false);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    public async Task<StatementDraftDto?> SetEntrySplitDraftAsync(Guid draftId, Guid entryId, Guid? splitDraftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }

        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }
        if (draft.Status != StatementDraftStatus.Draft) { return null; }

        if (splitDraftId != null)
        {
            if (entry.ContactId == null)
            {
                throw new InvalidOperationException("Contact required for split.");
            }
            var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == entry.ContactId && c.OwnerUserId == ownerUserId, ct);
            if (contact == null || !contact.IsPaymentIntermediary)
            {
                throw new InvalidOperationException("Contact is not a payment intermediary.");
            }
            var splitDraft = await _db.StatementDrafts.FirstOrDefaultAsync(d => d.Id == splitDraftId && d.OwnerUserId == ownerUserId, ct);
            if (splitDraft == null) { throw new InvalidOperationException("Split draft not found."); }
            if (splitDraft.DetectedAccountId != null) { throw new InvalidOperationException("Split draft must not have an account assigned."); }
            bool inUse = await _db.StatementDraftEntries.AnyAsync(e => e.SplitDraftId == splitDraftId, ct);
            if (inUse) { throw new InvalidOperationException("Split draft already linked."); }
            entry.AssignSplitDraft(splitDraftId.Value);
        }
        else
        {
            entry.ClearSplitDraft();
        }
        await _db.SaveChangesAsync(ct);
        if (splitDraftId != null)
        {
            await ReevaluateParentEntryStatusAsync(ownerUserId, splitDraftId.Value, ct);
        }
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    // Die Konstruktor-Signatur von StatementDraftEntryDto verlangt zusätzliche Parameter für Security-Informationen.
    // Füge die fehlenden Argumente (SecurityId, SecurityTransactionType, SecurityQuantity, SecurityFeeAmount, SecurityTaxAmount) überall dort hinzu, wo StatementDraftEntryDto instanziiert wird.

    private static StatementDraftDto Map(StatementDraft draft)
    {
        var total = draft.Entries.Sum(e => e.Amount);
        return new StatementDraftDto(
            draft.Id,
            draft.OriginalFileName,
            draft.DetectedAccountId,
            draft.Status,
            total,
            false,
            null,
            null,
            null,
            draft.Entries.Select(e => new StatementDraftEntryDto(
                e.Id,
                e.BookingDate,
                e.ValutaDate,
                e.Amount,
                e.CurrencyCode,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                e.IsAnnounced,
                e.IsCostNeutral,
                e.Status,
                e.ContactId,
                e.SavingsPlanId,
                e.SplitDraftId,
                e.SecurityId,
                e.SecurityTransactionType,
                e.SecurityQuantity,
                e.SecurityFeeAmount,
                e.SecurityTaxAmount)).ToList());
    }

    // Mapping mit Split-Infos
    private static StatementDraftDto Map(StatementDraft draft, IDictionary<Guid, dynamic> splitRefLookup)
    {
        var total = draft.Entries.Sum(e => e.Amount);
        dynamic? refInfo = null;
        splitRefLookup.TryGetValue(draft.Id, out refInfo);
        Guid? parentDraftId = refInfo?.DraftId;
        Guid? parentEntryId = refInfo?.Id;
        decimal? parentEntryAmount = refInfo?.Amount;

        return new StatementDraftDto(
            draft.Id,
            draft.OriginalFileName,
            draft.DetectedAccountId,
            draft.Status,
            total,
            parentDraftId != null,
            parentDraftId,
            parentEntryId,
            parentEntryAmount,
            draft.Entries.Select(e => new StatementDraftEntryDto(
                e.Id,
                e.BookingDate,
                e.ValutaDate,
                e.Amount,
                e.CurrencyCode,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                e.IsAnnounced,
                e.IsCostNeutral,
                e.Status,
                e.ContactId,
                e.SavingsPlanId,
                e.SplitDraftId,
                e.SecurityId,
                e.SecurityTransactionType,
                e.SecurityQuantity,
                e.SecurityFeeAmount,
                e.SecurityTaxAmount)).ToList());
    }

    // UpdateEntryCoreAsync: StatementDraftEntryDto Konstruktor anpassen
    public async Task<StatementDraftEntryDto?> UpdateEntryCoreAsync(Guid draftId, Guid entryId, Guid ownerUserId, DateTime bookingDate, DateTime? valutaDate, decimal amount, string subject, string? recipientName, string? currencyCode, string? bookingDescription, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }
        if (draft.Status != StatementDraftStatus.Draft) { return null; }
        entry.UpdateCore(bookingDate, valutaDate, amount, subject, recipientName, currencyCode, bookingDescription);
        await _db.SaveChangesAsync(ct);
        if (await _db.StatementDraftEntries.AnyAsync(e => e.SplitDraftId == draft.Id, ct))
        {
            await ReevaluateParentEntryStatusAsync(ownerUserId, draft.Id, ct);
        }
        if (entry.SplitDraftId != null)
        {
            await ReevaluateParentEntryStatusAsync(ownerUserId, entry.SplitDraftId.Value, ct);
        }
        return new StatementDraftEntryDto(
            entry.Id,
            entry.BookingDate,
            entry.ValutaDate,
            entry.Amount,
            entry.CurrencyCode,
            entry.Subject,
            entry.RecipientName,
            entry.BookingDescription,
            entry.IsAnnounced,
            entry.IsCostNeutral,
            entry.Status,
            entry.ContactId,
            entry.SavingsPlanId,
            entry.SplitDraftId,
            entry.SecurityId,
            entry.SecurityTransactionType,
            entry.SecurityQuantity,
            entry.SecurityFeeAmount,
            entry.SecurityTaxAmount);
    }

    private async Task ReevaluateParentEntryStatusAsync(Guid ownerUserId, Guid splitDraftId, CancellationToken ct)
    {
        var parentEntry = await _db.StatementDraftEntries.FirstOrDefaultAsync(e => e.SplitDraftId == splitDraftId, ct);
        if (parentEntry == null) { return; }
        var parentDraft = await _db.StatementDrafts.Include(d => d.Entries).FirstOrDefaultAsync(d => d.Id == parentEntry.DraftId && d.OwnerUserId == ownerUserId, ct);
        if (parentDraft == null) { return; }
        var total = await _db.StatementDraftEntries.Where(e => e.DraftId == splitDraftId).SumAsync(e => e.Amount, ct);
        if (total == parentEntry.Amount && parentEntry.ContactId != null && parentEntry.Status != StatementDraftEntryStatus.Accounted)
        {
            parentEntry.MarkAccounted(parentEntry.ContactId.Value);
        }
        else if (total != parentEntry.Amount && parentEntry.Status == StatementDraftEntryStatus.Accounted)
        {
            parentEntry.ResetOpen();
            if (parentEntry.ContactId != null)
            {
                parentEntry.AssignContactWithoutAccounting(parentEntry.ContactId.Value);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task ClassifyInternalAsync(StatementDraft draft, Guid ownerUserId, CancellationToken ct)
    {
        await ClassifyHeader(draft, ownerUserId, ct);

        // Preload data needed for classification
        var contacts = await _db.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .ToListAsync(ct);
        var selfContact = contacts.First(c => c.Type == ContactType.Self);
        var aliasLookup = await _db.AliasNames.AsNoTracking()
            .Where(a => contacts.Select(c => c.Id).Contains(a.ContactId))
            .GroupBy(a => a.ContactId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Pattern).ToList(), ct);
        var savingPlans = await _db.SavingsPlans.AsNoTracking()
            .Where(sp => sp.OwnerUserId == ownerUserId && sp.IsActive)
            .ToListAsync(ct);

        // Duplicate check basis: existing statement entries for detected account (last 180 days)
        List<(DateTime BookingDate, decimal Amount, string Subject)> existing = new();
        if (draft.DetectedAccountId != null)
        {
            var since = DateTime.UtcNow.AddDays(-180);
            var tempExisting = await _db.StatementEntries.AsNoTracking()
                .Where(se => se.BookingDate >= since)
                .Select(se => new { se.BookingDate, se.Amount, se.Subject })
                .ToListAsync(ct);
            existing = tempExisting
                .Select(x => (x.BookingDate.Date, x.Amount, x.Subject))
                .ToList();
        }

        // BankContactId für Fallback ermitteln
        Guid? bankContactId = null;
        if (draft.DetectedAccountId != null)
        {
            bankContactId = await _db.Accounts
                .Where(a => a.Id == draft.DetectedAccountId)
                .Select(a => (Guid?)a.BankContactId)
                .FirstOrDefaultAsync(ct);
        }

        foreach (var entry in draft.Entries)
        {
            // Reset to base status first (keep AlreadyBooked if previously flagged)
            if (entry.Status != StatementDraftEntryStatus.AlreadyBooked)
            {
                entry.ResetOpen();
            }

            // Duplicate (already booked)? – naive match BookingDate+Amount+Subject
            if (existing.Any(x => x.BookingDate == entry.BookingDate.Date && x.Amount == entry.Amount && string.Equals(x.Subject, entry.Subject, StringComparison.OrdinalIgnoreCase)))
            {
                entry.MarkAlreadyBooked();
                continue;
            }

            if (entry.IsAnnounced)
            {
                // Announced stays unless we can fully account it with a contact
            }

            TryAutoAssignContact(contacts, aliasLookup, bankContactId, selfContact, entry);
            TryAutoAssignSavingsPlan(entry, savingPlans, selfContact);
        }
    }

    private static void TryAutoAssignContact(List<Domain.Contacts.Contact> contacts, Dictionary<Guid, List<string>> aliasLookup, Guid? bankContactId, Domain.Contacts.Contact selfContact, StatementDraftEntry entry)
    {
        var normalizedRecipient = (entry.RecipientName ?? string.Empty).ToLowerInvariant().TrimEnd();
        Guid? matchedContactId = AssignContact(contacts, aliasLookup, bankContactId, entry, normalizedRecipient);
        var matchedContact = contacts.FirstOrDefault(c => c.Id == matchedContactId);
        if (matchedContact != null && matchedContact.IsPaymentIntermediary)
        {
            var normalizedSubject = (entry.Subject ?? string.Empty).ToLowerInvariant();
            matchedContactId = AssignContact(contacts, aliasLookup, bankContactId, entry, normalizedSubject);
        }
        else if (matchedContact != null && matchedContact.Type == ContactType.Bank && bankContactId != null && matchedContact.Id != bankContactId)
        {
            entry.MarkCostNeutral(true);
            entry.MarkAccounted(selfContact.Id);
        }
        else if (matchedContact != null)
        {
            if (matchedContact.Id == selfContact.Id)
            {
                entry.MarkCostNeutral(true);
            }
            entry.MarkAccounted(matchedContact.Id);
        }
    }

    private async Task ClassifyHeader(StatementDraft draft, Guid ownerUserId, CancellationToken ct)
    {
        // Account detection fallback if still not set: pick single account if only one exists
        if ((draft.DetectedAccountId == null) && (draft.AccountName != null))
        {
            var account = await _db.Accounts.AsNoTracking()
                .Where(a => a.OwnerUserId == ownerUserId && (a.Iban == draft.AccountName))
                .Select(a => new { a.Id })
                .FirstOrDefaultAsync(ct);
            if (account != null)
            {
                draft.SetDetectedAccount(account.Id);
            }
        }
        if (draft.DetectedAccountId == null && draft.AccountName == null)
        {
            var singleAccountId = await _db.Accounts.AsNoTracking()
                .Where(a => a.OwnerUserId == ownerUserId)
                .Select(a => a.Id)
                .ToListAsync(ct);
            if (singleAccountId.Count == 1)
            {
                draft.SetDetectedAccount(singleAccountId[0]);
            }
        }
    }

    private static Guid? AssignContact(
        List<Domain.Contacts.Contact> contacts,
        Dictionary<Guid, List<string>> aliasLookup,
        Guid? bankContactId,
        StatementDraftEntry entry,
        string searchText)
    {
        Guid? matchedContactId = contacts
            .Where(c => string.Equals(c.Name, searchText, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .FirstOrDefault();

        if (matchedContactId == Guid.Empty)
        {
            foreach (var kvp in aliasLookup)
            {
                foreach (var pattern in kvp.Value.Select(val => val.ToLowerInvariant()))
                {
                    if (string.IsNullOrWhiteSpace(pattern)) { continue; }
                    // Platzhalter in Regex umwandeln: * → .*, ? → .
                    var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(searchText, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        matchedContactId = kvp.Key;
                        break;
                    }
                }
                if (matchedContactId != Guid.Empty) { break; }
            }
        }
        var matchedContact = contacts.FirstOrDefault(c => c.Id == matchedContactId);
        // Fallback: Wenn kein Empfängername angegeben ist, setze Bankkontakt als Empfänger
        if (string.IsNullOrWhiteSpace(entry.RecipientName) && bankContactId != null && bankContactId != Guid.Empty)
        {
            entry.MarkAccounted(bankContactId.Value);
        }
        else if (matchedContactId != null && matchedContactId != Guid.Empty)
        {
            if (matchedContact != null && matchedContact.IsPaymentIntermediary)
                entry.AssignContactWithoutAccounting(matchedContact.Id);
            else
                entry.MarkAccounted(matchedContactId.Value);
        }

        return matchedContactId;
    }


    // Beispielhafte Implementierungsergänzung (Ausschnitt)
    public async Task<StatementDraft?> SetEntrySecurityAsync(
        Guid draftId,
        Guid entryId,
        Guid? securityId,
        SecurityTransactionType? transactionType,
        decimal? quantity,
        decimal? feeAmount,
        decimal? taxAmount,
        Guid userId,
        CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == userId, ct);

        if (draft == null) { return null; }

        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }

        // Optional: Validierung – nur wenn Empfänger = Bankkontakt
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, ct);
        if (draft.DetectedAccountId is null)
        {
            // Entfernen falls unzulässig
            entry.SetSecurity(null, null, null, null, null);
        }
        else if (account.BankContactId == null || entry.ContactId != account.BankContactId)
        {
            // Entfernen falls unzulässig
            entry.SetSecurity(null, null, null, null, null);
        }
        else
        {
            entry.SetSecurity(securityId, transactionType, quantity, feeAmount, taxAmount);
        }

        await _db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<DraftValidationResultDto> ValidateAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null)
        {
            return new DraftValidationResultDto(draftId, false, new List<DraftValidationMessageDto>{ new("DRAFT_NOT_FOUND","Error","Draft not found", draftId, null) });
        }

        var messages = new List<DraftValidationMessageDto>();

        // Determine if this draft is used as a split draft (referenced by a parent entry)
        var isSplitDraft = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SplitDraftId == draft.Id, ct);

        // Account assignment rules differ for split drafts
        if (isSplitDraft)
        {
            // Split draft MUST NOT have an account
            if (draft.DetectedAccountId != null)
            {
                messages.Add(new("SPLIT_DRAFT_HAS_ACCOUNT","Error","Ein zugeordneter Aufteilungs-Kontoauszug darf keinem Bankkonto zugeordnet sein.", draft.Id, null));
            }
        }
        else
        {
            // Normal draft MUST have an account
            if (draft.DetectedAccountId == null)
            {
                messages.Add(new("NO_ACCOUNT","Error","Dem Kontoauszug ist kein Bankkonto zugeordnet.", draft.Id, null));
            }
        }

        // Preload supporting data
        var accounts = await _db.Accounts.AsNoTracking().ToListAsync(ct);
        var contacts = await _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == ownerUserId).ToListAsync(ct);
        var selfContact = contacts.FirstOrDefault(c => c.Type == ContactType.Self);
        var savingsPlans = await _db.SavingsPlans.AsNoTracking().Where(sp => sp.OwnerUserId == ownerUserId).ToListAsync(ct);

        Domain.Contacts.Contact? bankContact = null;
        if (draft.DetectedAccountId is Guid accId)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accId);
            if (account != null)
            {
                bankContact = contacts.FirstOrDefault(c => c.Id == account.BankContactId);
            }
        }

        // For quick split draft totals
        Dictionary<Guid, decimal> draftTotalsCache = new();
        async Task<decimal> GetDraftTotal(Guid id)
        {
            if (draftTotalsCache.TryGetValue(id, out var total)) { return total; }
            total = await _db.StatementDraftEntries.AsNoTracking().Where(e => e.DraftId == id).SumAsync(e => e.Amount, ct);
            draftTotalsCache[id] = total; return total;
        }

        foreach (var entry in draft.Entries)
        {
            // Entry must have contact
            if (entry.ContactId == null)
            {
                messages.Add(new("ENTRY_NO_CONTACT","Error","Dem Eintrag ist kein Empfängerkontakt zugeordnet.", draft.Id, entry.Id));
            }
            else
            {
                var contact = contacts.FirstOrDefault(c => c.Id == entry.ContactId);
                if (contact != null && contact.IsPaymentIntermediary)
                {
                    // If payment intermediary -> split draft must be assigned
                    if (entry.SplitDraftId == null)
                    {
                        messages.Add(new("INTERMEDIARY_NO_SPLIT","Error","Empfängerkontakt ist Zahlungsvermittler – Aufteilungs-Auszug oder tatsächlicher Empfänger erforderlich.", draft.Id, entry.Id));
                    }
                }

                // Savings plan rules
                if (entry.SavingsPlanId != null)
                {
                    if (contact != null && contact.Type != ContactType.Self)
                    {
                        messages.Add(new("SAVINGSPLAN_INVALID_CONTACT","Error","Sparplan darf nur bei eigenem (Self) Kontakt zugeordnet sein.", draft.Id, entry.Id));
                    }
                }
                else if (contact != null && selfContact != null && contact.Id == selfContact.Id)
                {
                    messages.Add(new("SAVINGSPLAN_MISSING_FOR_SELF","Warning","Beim eigenen Kontakt ist kein Sparplan ausgewählt (Hinweis).", draft.Id, entry.Id));
                }

                // Security rules
                if (entry.SecurityId != null)
                {
                    if (bankContact == null || contact == null || contact.Id != bankContact.Id)
                    {
                        messages.Add(new("SECURITY_INVALID_CONTACT","Error","Wertpapiere dürfen nur beim Bankkontakt des Kontoauszugs zugewiesen sein.", draft.Id, entry.Id));
                    }
                    if (entry.SecurityTransactionType == null)
                    {
                        messages.Add(new("SECURITY_MISSING_TXTYPE","Error","Für ein zugewiesenes Wertpapier muss eine Buchungsart angegeben sein.", draft.Id, entry.Id));
                    }
                    if (entry.SecurityQuantity == null)
                    {
                        messages.Add(new("SECURITY_MISSING_QUANTITY","Error","Für ein zugewiesenes Wertpapier muss eine Menge angegeben sein.", draft.Id, entry.Id));
                    }
                }
            }

            // Split draft constraints
            if (entry.SplitDraftId != null)
            {
                var splitTotal = await GetDraftTotal(entry.SplitDraftId.Value);
                if (splitTotal != entry.Amount)
                {
                    messages.Add(new("SPLIT_AMOUNT_MISMATCH","Error","Summe des Aufteilungs-Auszugs entspricht nicht dem Betrag des Originaleintrags.", draft.Id, entry.Id));
                }

                // Validate split draft recursively (single level per spec; still run validation to reuse rules)
                var splitValidation = await ValidateAsync(entry.SplitDraftId.Value, ownerUserId, ct);
                foreach (var m in splitValidation.Messages)
                {
                    // propagate but mark as from split
                    messages.Add(new DraftValidationMessageDto(
                        m.Code,
                        m.Severity,
                        "[Split] " + m.Message,
                        m.DraftId,
                        m.EntryId));
                }
            }
        }

        var isValid = messages.All(m => m.Severity != "Error");
        return new DraftValidationResultDto(draft.Id, isValid, messages);
    }

    public async Task<DraftValidationResultDto> ValidateEntryAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct)
    {
        var full = await ValidateAsync(draftId, ownerUserId, ct);
        var filtered = full.Messages.Where(m => m.EntryId == entryId).ToList();
        var isValid = filtered.All(m => m.Severity != "Error");
        return new DraftValidationResultDto(draftId, isValid, filtered);
    }


    public async Task<BookingResult> BookAsync(Guid draftId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        var validation = await ValidateAsync(draftId, ownerUserId, ct);
        var hasErrors = validation.Messages.Any(m => m.Severity == "Error");
        var hasWarnings = validation.Messages.Any(m => m.Severity == "Warning");
        if (hasErrors) { return new BookingResult(false, hasWarnings, validation, null, null); }
        if (hasWarnings && !forceWarnings) { return new BookingResult(false, true, validation, null, null); }

        var draft = await _db.StatementDrafts.Include(d=>d.Entries)
            .FirstOrDefaultAsync(d=>d.Id==draftId && d.OwnerUserId==ownerUserId, ct);
        if (draft == null) { return new BookingResult(false, false, validation, null, null); }
        if (draft.DetectedAccountId == null) { return new BookingResult(false, false, validation, null, null); }

        // Create StatementImport similar to CommitAsync but enriched postings
        var import = new StatementImport(draft.DetectedAccountId.Value, ImportFormat.Csv, draft.OriginalFileName);
        _db.StatementImports.Add(import);
        await _db.SaveChangesAsync(ct);

        // Track created postings per entry (future linking if needed)
        foreach (var e in draft.Entries)
        {
            var groupId = Guid.NewGuid();
            // If entry has split draft assigned: amount moves to child entries; original becomes zero-based container
            decimal baseAmount = e.SplitDraftId != null ? 0m : e.Amount;

            // Common metadata
            var subj = e.Subject;
            var recip = e.RecipientName;
            var desc = e.BookingDescription;

            // Bank posting (always if account assigned)
            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Bank, draft.DetectedAccountId, null, null, null, e.BookingDate, baseAmount, subj, recip, desc, null).SetGroup(groupId));

            // Contact posting
            if (e.ContactId != null)
            {
                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, baseAmount, subj, recip, desc, null).SetGroup(groupId));
            }

            // SavingsPlan posting
            if (e.SavingsPlanId != null)
            {
                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.SavingsPlan, null, null, e.SavingsPlanId, null, e.BookingDate, baseAmount, subj, recip, desc, null).SetGroup(groupId));
            }

            // Security posting + fee/tax adjustments
            if (e.SecurityId != null)
            {
                var securityGroupId = Guid.NewGuid();
                var tradeNet = baseAmount;
                if (e.SecurityFeeAmount is decimal f) { tradeNet -= f; }
                if (e.SecurityTaxAmount is decimal t) { tradeNet -= t; }
                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tradeNet, subj, recip, desc, Domain.Postings.SecurityPostingSubType.Trade).SetGroup(securityGroupId));
                if (e.SecurityFeeAmount is decimal fee && fee != 0)
                {
                    _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, fee, subj, recip, "Fee", Domain.Postings.SecurityPostingSubType.Fee).SetGroup(securityGroupId));
                }
                if (e.SecurityTaxAmount is decimal tax && tax != 0)
                {
                    _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tax, subj, recip, "Tax", Domain.Postings.SecurityPostingSubType.Tax).SetGroup(securityGroupId));
                }
            }

            // If split draft: recursively add child postings
            if (e.SplitDraftId != null)
            {
                var childDraft = await _db.StatementDrafts.Include(x=>x.Entries).FirstOrDefaultAsync(x=>x.Id==e.SplitDraftId, ct);
                if (childDraft != null)
                {
                    foreach(var ce in childDraft.Entries)
                    {
                        var childAmount = ce.Amount;
                        var cSubj = ce.Subject; var cRecip = ce.RecipientName; var cDesc = ce.BookingDescription;
                        var childGroupId = Guid.NewGuid();
                        _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Bank, draft.DetectedAccountId, null, null, null, ce.BookingDate, childAmount, cSubj, cRecip, cDesc, null).SetGroup(childGroupId));
                        if (ce.ContactId != null)
                        {
                            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Contact, null, ce.ContactId, null, null, ce.BookingDate, childAmount, cSubj, cRecip, cDesc, null).SetGroup(childGroupId));
                        }
                        if (ce.SavingsPlanId != null)
                        {
                            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.SavingsPlan, null, null, ce.SavingsPlanId, null, ce.BookingDate, childAmount, cSubj, cRecip, cDesc, null).SetGroup(childGroupId));
                        }
                        if (ce.SecurityId != null)
                        {
                            var childSecurityGroupId = Guid.NewGuid();
                            var childNet = childAmount;
                            if (ce.SecurityFeeAmount is decimal cf) { childNet -= cf; }
                            if (ce.SecurityTaxAmount is decimal ctax) { childNet -= ctax; }
                            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, ce.SecurityId, ce.BookingDate, childNet, cSubj, cRecip, cDesc, Domain.Postings.SecurityPostingSubType.Trade).SetGroup(childSecurityGroupId));
                            if (ce.SecurityFeeAmount is decimal cfee && cfee != 0)
                            {
                                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, ce.SecurityId, ce.BookingDate, cfee, cSubj, cRecip, "Fee", Domain.Postings.SecurityPostingSubType.Fee).SetGroup(childSecurityGroupId));
                            }
                            if (ce.SecurityTaxAmount is decimal ctax2 && ctax2 != 0)
                            {
                                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, ce.SecurityId, ce.BookingDate, ctax2, cSubj, cRecip, "Tax", Domain.Postings.SecurityPostingSubType.Tax).SetGroup(childSecurityGroupId));
                            }
                        }
                    }
                }
            }
        }

        draft.MarkCommitted();
        await _db.SaveChangesAsync(ct);
        return new BookingResult(true, hasWarnings, validation, import.Id, draft.Entries.Count);
    }
}
