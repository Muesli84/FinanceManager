using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Numerics;
using System.Text.RegularExpressions;

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService
{
    private static string NormalizeUmlauts(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }
        return text
            .Replace("ä", "ae", StringComparison.OrdinalIgnoreCase)
            .Replace("ö", "oe", StringComparison.OrdinalIgnoreCase)
            .Replace("ü", "ue", StringComparison.OrdinalIgnoreCase)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);
    }

    public void TryAutoAssignSavingsPlan(StatementDraftEntry entry, IEnumerable<SavingsPlan> userPlans, Contact selfContact)
    {
        if (entry.ContactId is null) { return; }
        if (entry.ContactId != selfContact.Id) { return; }

        string Clean(string s) => Regex.Replace(s ?? string.Empty, "\\s+", string.Empty);

        var normalizedSubject = NormalizeUmlauts(entry.Subject).ToLowerInvariant();
        var normalizedSubjectNoSpaces = Clean(normalizedSubject);

        var matchingPlans = userPlans.Where(plan =>
        {
            if (string.IsNullOrWhiteSpace(plan.Name)) { return false; }
            var normalizedPlanName = Clean(NormalizeUmlauts(plan.Name).ToLowerInvariant());

            bool nameMatches = normalizedSubjectNoSpaces.Contains(normalizedPlanName);
            bool contractMatches = false;

            if (!nameMatches && !string.IsNullOrWhiteSpace(plan.ContractNumber))
            {
                var cn = plan.ContractNumber.Trim();
                var subjectForContract = Regex.Replace(entry.Subject ?? string.Empty, "[\\s-]", string.Empty, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                var cnNormalized = Regex.Replace(cn, "[\\s-]", string.Empty, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                contractMatches = subjectForContract.Contains(cnNormalized, StringComparison.OrdinalIgnoreCase);
            }
            return (nameMatches || contractMatches);
        }).ToList();

        if (matchingPlans.FirstOrDefault() is SavingsPlan plan)
            entry.AssignSavingsPlan(plan.Id);
        if (matchingPlans.Count > 1)
            entry.MarkNeedsCheck();
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
    private async Task ClassifyInternalAsync(StatementDraft draft, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        await ClassifyHeader(draft, ownerUserId, ct);

        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || e.Id == entryId)).ToListAsync(ct);

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

        // Securities für Auto-Matching laden (nur aktive)
        var securities = await _db.Securities.AsNoTracking()
            .Where(s => s.OwnerUserId == ownerUserId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

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

        Guid? bankContactId = null;
        if (draft.DetectedAccountId != null)
        {
            bankContactId = await _db.Accounts
                .Where(a => a.Id == draft.DetectedAccountId)
                .Select(a => (Guid?)a.BankContactId)
                .FirstOrDefaultAsync(ct);
        }

        foreach (var entry in entries)
        {
            if (entry.Status != StatementDraftEntryStatus.AlreadyBooked)
            {
                entry.ResetOpen();
            }

            if (existing.Any(x => x.BookingDate == entry.BookingDate.Date && x.Amount == entry.Amount && string.Equals(x.Subject, entry.Subject, StringComparison.OrdinalIgnoreCase)))
            {
                entry.MarkAlreadyBooked();
                continue;
            }

            if (entry.IsAnnounced)
            {
                // keep announced unless fully accounted
            }

            TryAutoAssignContact(contacts, aliasLookup, bankContactId, selfContact, entry);
            TryAutoAssignSavingsPlan(entry, savingPlans, selfContact);
            TryAutoAssignSecurity(securities, entry);
        }

        static void TryAutoAssignSecurity(IEnumerable<Domain.Securities.Security> securities, StatementDraftEntry entry)
        {
            // Helper zur Normalisierung (nur A-Z/0-9, Großschreibung, Umlaute vereinheitlichen)
            static string NormalizeForSecurityMatch(string? s)
            {
                var baseText = NormalizeUmlauts(s ?? string.Empty).ToUpperInvariant();
                return Regex.Replace(baseText, "[^A-Z0-9]", string.Empty, RegexOptions.CultureInvariant | RegexOptions.Compiled);
            }

            // Automatisierte Wertpapierzuordnung:
            // - Nur wenn bisher kein Wertpapier gesetzt ist
            // - Match anhand Identifier, AlphaVantageCode oder Name, die im Betreff / Beschreibung / Empfänger vorkommen
            if (entry.SecurityId != null || securities.Count() <= 0)
            {
                return;
            }
            var rawText = $"{entry.Subject} {entry.BookingDescription} {entry.RecipientName}";
            var haystack = NormalizeForSecurityMatch(rawText);

            bool Matches(string? probe)
            {
                var p = NormalizeForSecurityMatch(probe);
                if (string.IsNullOrEmpty(p)) { return false; }
                return haystack.Contains(p, StringComparison.Ordinal);
            }

            var matched = securities
                .Where(s =>
                    Matches(s.Identifier) ||
                    Matches(s.AlphaVantageCode) ||
                    Matches(s.Name))
                .ToList();

            if (matched.Count == 1)
            {
                entry.SetSecurity(matched[0].Id, null, null, null, null);
            }
            else if (matched.Count > 1)
            {
                var first = matched.First();
                entry.SetSecurity(first.Id, null, null, null, null);
                // Mehrdeutige Zuordnung → Status auf Offen lassen
                entry.ResetOpen();
            }
        }
    }

    private static void TryAutoAssignContact(List<Contact> contacts, Dictionary<Guid, List<string>> aliasLookup, Guid? bankContactId, Contact selfContact, StatementDraftEntry entry)
    {
        var normalizedRecipient = NormalizeUmlauts((entry.RecipientName ?? string.Empty).ToLowerInvariant().TrimEnd());
        Guid? matchedContactId = AssignContact(contacts, aliasLookup, bankContactId, entry, normalizedRecipient);
        var matchedContact = contacts.FirstOrDefault(c => c.Id == matchedContactId);
        if (matchedContact != null && matchedContact.IsPaymentIntermediary)
        {
            var normalizedSubject = (entry.Subject ?? string.Empty).ToLowerInvariant().TrimEnd();
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
        if (draft.DetectedAccountId == null && string.IsNullOrWhiteSpace(draft.AccountName))
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
        List<Contact> contacts,
        Dictionary<Guid, List<string>> aliasLookup,
        Guid? bankContactId,
        StatementDraftEntry entry,
        string searchText)
    {
        Guid? matchedContactId = contacts
            .Where(c => string.Equals(NormalizeUmlauts(c.Name), searchText, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Id)
            .FirstOrDefault();
        Guid? secondaryContactId = matchedContactId = contacts
                .Where(c => searchText.Contains(NormalizeUmlauts(c.Name), StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .FirstOrDefault();

        for (int idxMode = 0; idxMode < 2; idxMode++)         {
            if (matchedContactId != Guid.Empty) { break; }

            if (idxMode == 1)
            {
                searchText = Regex.Replace(searchText, "\\s+", string.Empty);
            }

            foreach (var kvp in aliasLookup) 
            {
                foreach (var pattern in kvp.Value.Select(val => val.ToLowerInvariant()))
                {
                    if (string.IsNullOrWhiteSpace(pattern)) { continue; }
                    var regexPattern = "^" + Regex.Escape(pattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";
                    if (Regex.IsMatch(searchText, regexPattern, RegexOptions.IgnoreCase))
                    {
                        matchedContactId = kvp.Key;
                        break;
                    }
                }
                if (matchedContactId != Guid.Empty) { break; }
            }
        }
        if (matchedContactId == null || matchedContactId == Guid.Empty) matchedContactId = secondaryContactId;

        var matchedContact = contacts.FirstOrDefault(c => c.Id == matchedContactId);
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
}
