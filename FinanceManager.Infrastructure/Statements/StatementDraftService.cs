using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Reader;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService : IStatementDraftService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileReader> _statementFileReaders;

    public StatementDraftService(AppDbContext db)
    {
        _db = db;
        _statementFileReaders = new List<IStatementFileReader>
        {
            new ING_StatementFileReader(),
            new Barclays_StatementFileReader(),
            new BackupStatementFileReader()
        };
    }

    private static string NormalizeUmlauts(string text)
    {
        if (string.IsNullOrEmpty(text)) { return string.Empty; }
        return text
            .Replace("ä", "ae", StringComparison.OrdinalIgnoreCase)
            .Replace("ö", "oe", StringComparison.OrdinalIgnoreCase)
            .Replace("ü", "ue", StringComparison.OrdinalIgnoreCase)
            .Replace("ß", "ss", StringComparison.OrdinalIgnoreCase);
    }

    public async IAsyncEnumerable<StatementDraftDto> CreateDraftAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct)
    {
        var parsedDraft = _statementFileReaders
            .Select(reader => reader.Parse(originalFileName, fileBytes))
            .Where(result => result is not null && result.Movements.Any())
            .FirstOrDefault();

        if (parsedDraft is null)
        {
            throw new InvalidOperationException("No valid statement file reader found or no movements detected.");
        }

        StatementDraft draft = await CreateDraftHeader(ownerUserId, originalFileName, fileBytes, parsedDraft, ct);
        foreach (var movement in parsedDraft.Movements)
        {
            draft.AddEntry(movement.BookingDate, movement.Amount, movement.Subject ?? string.Empty, movement.Counterparty, movement.ValutaDate, movement.CurrencyCode, movement.PostingDescription, movement.IsPreview, false);
            if (draft.Entries.Count == 100)
            {
                yield return await FinishDraftAsync(draft, ownerUserId, ct);
                draft = await CreateDraftHeader(ownerUserId, originalFileName, fileBytes, parsedDraft, ct);
            }
        }
        yield return await FinishDraftAsync(draft, ownerUserId, ct);
    }

    private async Task<StatementDraft> CreateDraftHeader(Guid ownerUserId, string originalFileName, byte[] fileBytes, StatementParseResult parsedDraft, CancellationToken ct)
    {
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

        return draft;
    }

    private async Task<StatementDraftDto> FinishDraftAsync(StatementDraft draft, Guid ownerUserId, CancellationToken ct)
    {
        _db.StatementDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return Map(draft);
    }

    public async Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, CancellationToken ct)
    {
        var drafts = await _db.StatementDrafts
            .Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft)
            .OrderByDescending(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        var splitLinks = await _db.StatementDraftEntries
            .Where(e => e.SplitDraftId != null)
            .Select(e => new { e.Id, e.DraftId, e.SplitDraftId, e.Amount })
            .ToListAsync(ct);

        var bySplitId = splitLinks
            .GroupBy(x => x.SplitDraftId!.Value)
            .ToDictionary(g => g.Key, g => (dynamic)g.First());

        return drafts.Select(d => Map(d, bySplitId)).ToList();
    }

    public async Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, int skip, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 50);
        var query = _db.StatementDrafts
            .Include(d => d.Entries)
            .Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft)
            .OrderByDescending(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .Skip(skip)
            .Take(take)
            .AsNoTracking();

        var drafts = await query.ToListAsync(ct);

        var ids = drafts.Select(d => d.Id).ToList();
        var splitLinks = await _db.StatementDraftEntries
            .Where(e => e.SplitDraftId != null && ids.Contains(e.SplitDraftId.Value))
            .Select(e => new { e.Id, e.DraftId, e.SplitDraftId, e.Amount })
            .ToListAsync(ct);

        var bySplitId = splitLinks
            .GroupBy(x => x.SplitDraftId!.Value)
            .ToDictionary(g => g.Key, g => (dynamic)g.First());

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
            return Map(draft);
        }

        var dict = new Dictionary<Guid, dynamic> { { draft.Id, (dynamic)splitRef } };
        return Map(draft, dict);
    }

    public async Task<StatementDraftDto?> GetDraftHeaderAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }

        var splitRef = await _db.StatementDraftEntries
            .Where(e => e.SplitDraftId == draft.Id)
            .Select(e => new { e.DraftId, e.Id, e.Amount })
            .FirstOrDefaultAsync(ct);

        if (splitRef == null)
        {
            return Map(draft);
        }

        var dict = new Dictionary<Guid, dynamic> { { draft.Id, (dynamic)splitRef } };
        return Map(draft, dict);
    }

    public async Task<IEnumerable<StatementDraftEntryDto>> GetDraftEntriesAsync(Guid draftId, CancellationToken ct)
    {
        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draftId).ToListAsync(ct);
        return entries.Select(e => Map(e));
    }

    public async Task<StatementDraftEntryDto?> GetDraftEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var draftEntry = await _db.StatementDraftEntries.FirstOrDefaultAsync(e => e.DraftId == draftId && e.Id == entryId, ct);
        return Map(draftEntry);
    }

    public async Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        _db.Entry(draft.AddEntry(bookingDate, amount, subject)).State = EntityState.Added;
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        if (await _db.StatementDraftEntries.AnyAsync(e => e.SplitDraftId == draft.Id, ct))
        {
            await ReevaluateParentEntryStatusAsync(ownerUserId, draft.Id, ct);
        }
        return await GetDraftAsync(draftId, ownerUserId, ct);
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

    public async Task<StatementDraftDto?> ClassifyAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        await ClassifyInternalAsync(draft, entryId, ownerUserId, ct);
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
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draft.Id, ownerUserId, ct);
    }

    public void TryAutoAssignSavingsPlan(StatementDraftEntry entry, IEnumerable<SavingsPlan> userPlans, Contact selfContact)
    {
        if (entry.ContactId is null) { return; }
        if (entry.ContactId != selfContact.Id) { return; }

        string Clean(string s) => Regex.Replace(s ?? string.Empty, "\\s+", string.Empty);

        var normalizedSubject = NormalizeUmlauts(entry.Subject).ToLowerInvariant();
        var normalizedSubjectNoSpaces = Clean(normalizedSubject);

        foreach (var plan in userPlans)
        {
            if (string.IsNullOrWhiteSpace(plan.Name)) { continue; }
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

            if (nameMatches || contractMatches)
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
            draft.Entries.Select(e => Map(e)).ToList());
    }

    private static StatementDraftEntryDto Map(StatementDraftEntry e)
    {
        return new StatementDraftEntryDto(
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
            e.SecurityTaxAmount);
    }

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

    public async Task<StatementDraftEntryDto?> UpdateEntryCoreAsync(Guid draftId, Guid entryId, Guid ownerUserId, DateTime bookingDate, DateTime? valutaDate, decimal amount, string subject, string? recipientName, string? currencyCode, string? bookingDescription, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        var entry = await _db.StatementDraftEntries.FirstOrDefaultAsync(e => e.DraftId == draftId && e.Id == entryId, ct);
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
        }
    }

    private static void TryAutoAssignContact(List<Contact> contacts, Dictionary<Guid, List<string>> aliasLookup, Guid? bankContactId, Contact selfContact, StatementDraftEntry entry)
    {
        var normalizedRecipient = (entry.RecipientName ?? string.Empty).ToLowerInvariant().TrimEnd();
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

        if (matchedContactId == Guid.Empty)
        {
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

        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, ct);
        if (draft.DetectedAccountId is null)
        {
            entry.SetSecurity(null, null, null, null, null);
        }
        else if (account.BankContactId == null || entry.ContactId != account.BankContactId)
        {
            entry.SetSecurity(null, null, null, null, null);
        }
        else
        {
            entry.SetSecurity(securityId, transactionType, quantity, feeAmount, taxAmount);
        }

        await _db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<DraftValidationResultDto> ValidateAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        return await InternalValidateAsync(draftId, entryId, ownerUserId, ct, new HashSet<Guid>());
    }

    public async Task<DraftValidationResultDto> InternalValidateAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct, HashSet<Guid> visited)
    {
        if (!visited.Add(draftId))
        {
            return new DraftValidationResultDto(draftId, false, new List<DraftValidationMessageDto> { new("SPLIT_CYCLE_DETECTED", "Error", "Zirkuläre Referenz bei Aufteilungs-Auszügen erkannt.", draftId, null) });
        }

        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null)
        {
            return new DraftValidationResultDto(draftId, false, new List<DraftValidationMessageDto> { new("DRAFT_NOT_FOUND", "Error", "Draft not found", draftId, null) });
        }
        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || e.Id == entryId)).ToListAsync(ct);

        var messages = new List<DraftValidationMessageDto>();

        var isSplitDraft = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SplitDraftId == draft.Id, ct);
        if (isSplitDraft)
        {
            if (draft.DetectedAccountId != null)
            {
                messages.Add(new("SPLIT_DRAFT_HAS_ACCOUNT", "Error", "Ein zugeordneter Aufteilungs-Kontoauszug darf keinem Bankkonto zugeordnet sein.", draft.Id, null));
            }
        }
        else
        {
            if (draft.DetectedAccountId == null)
            {
                messages.Add(new("NO_ACCOUNT", "Error", "Dem Kontoauszug ist kein Bankkonto zugeordnet.", draft.Id, null));
            }
        }

        var accounts = await _db.Accounts.AsNoTracking().ToListAsync(ct);
        var contacts = await _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == ownerUserId).ToListAsync(ct);
        var selfContact = contacts.FirstOrDefault(c => c.Type == ContactType.Self);

        Contact? bankContact = null;
        if (draft.DetectedAccountId is Guid accId)
        {
            var account = accounts.FirstOrDefault(a => a.Id == accId);
            if (account != null)
            {
                bankContact = contacts.FirstOrDefault(c => c.Id == account.BankContactId);
            }
        }

        Dictionary<Guid, decimal> draftTotalsCache = new();
        async Task<decimal> GetDraftTotal(Guid id)
        {
            if (draftTotalsCache.TryGetValue(id, out var total)) { return total; }
            total = await _db.StatementDraftEntries.AsNoTracking().Where(e => e.DraftId == id).SumAsync(e => e.Amount, ct);
            draftTotalsCache[id] = total; return total;
        }

        foreach (var entry in entries)
        {
            if (entry.ContactId == null)
            {
                messages.Add(new("ENTRY_NO_CONTACT", "Error", "Dem Eintrag ist kein Empfängerkontakt zugeordnet.", draft.Id, entry.Id));
            }
            else
            {
                var contact = contacts.FirstOrDefault(c => c.Id == entry.ContactId);
                if (contact != null && contact.IsPaymentIntermediary)
                {
                    if (entry.SplitDraftId == null)
                    {
                        messages.Add(new("INTERMEDIARY_NO_SPLIT", "Error", "Empfängerkontakt ist Zahlungsvermittler – Aufteilungs-Auszug oder tatsächlicher Empfänger erforderlich.", draft.Id, entry.Id));
                    }
                }

                if (entry.SavingsPlanId != null)
                {
                    if (contact != null && contact.Type != ContactType.Self)
                    {
                        messages.Add(new("SAVINGSPLAN_INVALID_CONTACT", "Error", "Sparplan darf nur bei eigenem (Self) Kontakt zugeordnet sein.", draft.Id, entry.Id));
                    }
                }
                else if (contact != null && selfContact != null && contact.Id == selfContact.Id)
                {
                    messages.Add(new("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", "Beim eigenen Kontakt ist kein Sparplan ausgewählt (Hinweis).", draft.Id, entry.Id));
                }

                if (entry.SecurityId != null)
                {
                    if (bankContact == null || contact == null || contact.Id != bankContact.Id)
                    {
                        messages.Add(new("SECURITY_INVALID_CONTACT", "Error", "Wertpapiere dürfen nur beim Bankkontakt des Kontoauszugs zugewiesen sein.", draft.Id, entry.Id));
                    }
                    if (entry.SecurityTransactionType == null)
                    {
                        messages.Add(new("SECURITY_MISSING_TXTYPE", "Error", "Für ein zugewiesenes Wertpapier muss eine Buchungsart angegeben sein.", draft.Id, entry.Id));
                    }
                    if (entry.SecurityQuantity == null)
                    {
                        messages.Add(new("SECURITY_MISSING_QUANTITY", "Error", "Für ein zugewiesenes Wertpapier muss eine Menge angegeben sein.", draft.Id, entry.Id));
                    }
                    if ((entry.SecurityTaxAmount ?? 0m) + (entry.SecurityFeeAmount ?? 0m) > entry.Amount)
                    {
                        messages.Add(new("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Für ein zugewiesenes Wertpapier dürfen Steuer und Gebühr nicht größer sein, als der gebuchte Betrag.", draft.Id, entry.Id));
                    }
                }
            }

            if (entry.SplitDraftId != null)
            {
                var splitTotal = await GetDraftTotal(entry.SplitDraftId.Value);
                if (splitTotal != entry.Amount)
                {
                    messages.Add(new("SPLIT_AMOUNT_MISMATCH", "Error", "Summe des Aufteilungs-Auszugs entspricht nicht dem Betrag des Originaleintrags.", draft.Id, entry.Id));
                }

                var splitValidation = await InternalValidateAsync(entry.SplitDraftId.Value, null, ownerUserId, ct, visited);
                foreach (var m in splitValidation.Messages)
                {
                    messages.Add(new DraftValidationMessageDto(
                        m.Code,
                        m.Severity,
                        "[Split] " + m.Message,
                        m.DraftId,
                        m.EntryId));
                }
            }
        }

        // Savings plan goal info/warning
        var planIds = entries.Where(e => e.SavingsPlanId != null).Select(e => e.SavingsPlanId!.Value).Distinct().ToList();
        if (planIds.Count > 0)
        {
            var plans = await _db.SavingsPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToListAsync(ct);
            var contribByPlan = entries
                .Where(e => e.SavingsPlanId != null)
                .GroupBy(e => e.SavingsPlanId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            foreach (var plan in plans)
            {
                if (plan.TargetAmount is not decimal target) { continue; }
                var current = await _db.Postings.AsNoTracking()
                    .Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                var planned = contribByPlan.TryGetValue(plan.Id, out var val) ? val : 0m;
                if (planned <= 0) { continue; }
                var remaining = target - current;
                if (remaining > 0m && planned == remaining)
                {
                    messages.Add(new("SAVINGSPLAN_GOAL_REACHED_INFO", "Information", $"Mit den Buchungen in diesem Auszug wird das Sparziel des Sparplans '{plan.Name}' erreicht.", draft.Id, null));
                }
                else if (remaining > 0m && planned > remaining)
                {
                    messages.Add(new("SAVINGSPLAN_GOAL_EXCEEDED", "Warning", $"Die geplanten Buchungen überschreiten das Sparziel des Sparplans '{plan.Name}'.", draft.Id, null));
                }
            }
        }

        // Additional info: due savings plans not yet posted this month and not assigned in open drafts
        if (entries.Count > 0)
        {
            var latestBookingDate = entries.Max(e => e.BookingDate);
            var monthStart = new DateTime(latestBookingDate.Year, latestBookingDate.Month, 1);
            var nextMonth = monthStart.AddMonths(1);

            DateTime AdjustDueDate(DateTime d)
            {
                // If due date falls on weekend, treat as previous Friday
                if (d.DayOfWeek == DayOfWeek.Saturday) { return d.AddDays(-1); }
                if (d.DayOfWeek == DayOfWeek.Sunday) { return d.AddDays(-2); }
                return d;
            }

            var allUserPlans = await _db.SavingsPlans.AsNoTracking()
                .Where(p => p.OwnerUserId == ownerUserId && p.IsActive && p.TargetAmount != null && p.TargetDate != null)
                .ToListAsync(ct);

            foreach (var plan in allUserPlans)
            {
                var effectiveDue = AdjustDueDate(plan.TargetDate!.Value.Date);
                if (effectiveDue > latestBookingDate.Date) { continue; }

                // Target not yet reached
                var currentAmount = await _db.Postings.AsNoTracking()
                    .Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                var target = plan.TargetAmount!.Value;
                if (currentAmount >= target) { continue; }

                // No posting in the draft's month
                var hasPostingThisMonth = await _db.Postings.AsNoTracking()
                    .AnyAsync(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan && p.BookingDate >= monthStart && p.BookingDate < nextMonth, ct);
                if (hasPostingThisMonth) { continue; }

                // Not assigned in any open draft
                var assignedInOpenDraft = await _db.StatementDraftEntries
                    .Join(_db.StatementDrafts, e => e.DraftId, d => d.Id, (e, d) => new { e, d })
                    .AnyAsync(x => x.d.OwnerUserId == ownerUserId && x.d.Status == StatementDraftStatus.Draft && x.e.SavingsPlanId == plan.Id, ct);
                if (assignedInOpenDraft) { continue; }

                messages.Add(new("SAVINGSPLAN_DUE", "Information", $"Sparplan '{plan.Name}' ist fällig (Fälligkeitsdatum: {effectiveDue:d}).", draft.Id, null));
            }
        }

        var isValid = messages.All(m => m.Severity != "Error");
        return new DraftValidationResultDto(draft.Id, isValid, messages);
    }

    public async Task<BookingResult> BookAsync(Guid draftId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        var validation = await ValidateAsync(draftId, null, ownerUserId, ct);
        var hasErrors = validation.Messages.Any(m => m.Severity == "Error");
        var hasWarnings = validation.Messages.Any(m => m.Severity == "Warning");
        if (hasErrors) { return new BookingResult(false, hasWarnings, validation, null, null, null); }
        if (hasWarnings && !forceWarnings) { return new BookingResult(false, true, validation, null, null, null); }

        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.DetectedAccountId == null) { return new BookingResult(false, false, validation, null, null, null); }

        var import = new StatementImport(draft.DetectedAccountId.Value, ImportFormat.Csv, draft.OriginalFileName);
        _db.StatementImports.Add(import);
        await _db.SaveChangesAsync(ct);

        var updatedPlanIds = new HashSet<Guid>();
        List<StatementDraft> bookedDrafts = new();
        foreach (var e in draft.Entries)
        {
            bookedDrafts.AddRange(await BookEntryAsync(draft, import, e, updatedPlanIds, ct));
        }

        draft.MarkCommitted();
        foreach (var bd in bookedDrafts) { bd.MarkCommitted(); }
        await _db.SaveChangesAsync(ct);

        var nextJournal = await _db.StatementDrafts.OrderBy(d => d.Id).Where(d => d.Id > draftId && d.Status == StatementDraftStatus.Draft).FirstOrDefaultAsync(ct);
        if (nextJournal is null)
            nextJournal = await _db.StatementDrafts.OrderBy(d => d.Id).Where(d => d.Status == StatementDraftStatus.Draft).LastOrDefaultAsync(ct);

        return new BookingResult(true, hasWarnings, validation, import.Id, draft.Entries.Count, nextJournal?.Id);
    }

    private static int GetMonthsToAdd(SavingsPlanInterval interval)
    {
        return interval switch
        {
            SavingsPlanInterval.Monthly => 1,
            SavingsPlanInterval.BiMonthly => 2,
            SavingsPlanInterval.Quarterly => 3,
            SavingsPlanInterval.SemiAnnually => 6,
            SavingsPlanInterval.Annually => 12,
            _ => 0
        };
    }

    private async Task<IEnumerable<StatementDraft>> BookEntryAsync(StatementDraft draft, StatementImport import, StatementDraftEntry e, HashSet<Guid> updatedPlanIds, CancellationToken ct)
    {
        var groupId = Guid.NewGuid();
        decimal baseAmount = e.SplitDraftId != null ? 0m : e.Amount;
        var subj = e.Subject; var recip = e.RecipientName; var desc = e.BookingDescription;

        _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Bank, draft.DetectedAccountId, null, null, null, e.BookingDate, baseAmount, subj, recip, desc, null).SetGroup(groupId));
        if (e.ContactId != null)
        {
            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, baseAmount, subj, recip, desc, null).SetGroup(groupId));
        }
        if (e.SavingsPlanId != null)
        {
            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.SavingsPlan, null, null, e.SavingsPlanId, null, e.BookingDate, -baseAmount, subj, recip, desc, null).SetGroup(groupId));

            // Extend due date for recurring plans if due reached and not already updated in this booking
            var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == e.SavingsPlanId, ct);
            if (plan != null && plan.Type == SavingsPlanType.Recurring && plan.TargetDate != null && plan.Interval != null && !updatedPlanIds.Contains(plan.Id))
            {
                if (plan.TargetDate!.Value.Date <= e.BookingDate.Date)
                {
                    var months = GetMonthsToAdd(plan.Interval.Value);
                    if (months > 0)
                    {
                        plan.SetTarget(plan.TargetAmount, plan.TargetDate.Value.AddMonths(months));
                        updatedPlanIds.Add(plan.Id);
                    }
                }
            }
        }
        if (e.SecurityId != null)
        {
            var securityGroupId = Guid.NewGuid();
            var tradeNet = baseAmount;
            if (e.SecurityFeeAmount is decimal f) { tradeNet -= f; }
            if (e.SecurityTaxAmount is decimal t) { tradeNet -= t; }
            _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tradeNet, subj, recip, desc, SecurityPostingSubType.Trade).SetGroup(securityGroupId));
            if (e.SecurityFeeAmount is decimal fee && fee != 0)
            {
                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, fee, subj, recip, "Fee", SecurityPostingSubType.Fee).SetGroup(securityGroupId));
            }
            if (e.SecurityTaxAmount is decimal tax && tax != 0)
            {
                _db.Postings.Add(new Domain.Postings.Posting(import.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tax, subj, recip, "Tax", SecurityPostingSubType.Tax).SetGroup(securityGroupId));
            }
        }

        List<StatementDraft> bookedDrafts = new();
        if (e.SplitDraftId != null)
        {
            var childDraft = await _db.StatementDrafts.Include(x => x.Entries).FirstOrDefaultAsync(x => x.Id == e.SplitDraftId, ct);
            if (childDraft != null)
            {
                foreach (var ce in childDraft.Entries)
                {
                    bookedDrafts.AddRange(await BookEntryAsync(childDraft, import, ce, updatedPlanIds, ct));
                }
                bookedDrafts.Add(childDraft);
            }
        }
        return bookedDrafts;
    }
}
