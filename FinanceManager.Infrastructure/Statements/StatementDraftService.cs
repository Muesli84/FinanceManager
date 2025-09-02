using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Reader;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Statements;

public sealed class StatementDraftService : IStatementDraftService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileReader> _statementFileReaders;
    public StatementDraftService(AppDbContext db) { _db = db; _statementFileReaders = new List<IStatementFileReader>() { new ING_StatementFileReader() }; }

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

        var draft = new StatementDraft(ownerUserId, originalFileName);

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
            draft.AddEntry(movement.BookingDate, movement.Amount, movement.Subject ?? string.Empty, movement.Counterparty, movement.ValutaDate, movement.CurrencyCode, movement.PostingDescription, movement.IsPreview);
        }

        _db.StatementDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        // Auto classify after creation
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return Map(draft);
    }

    public async Task<StatementDraftDto?> GetDraftAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        return draft == null ? null : Map(draft);
    }

    public async Task<IReadOnlyList<StatementDraftDto>> GetOpenDraftsAsync(Guid ownerUserId, CancellationToken ct)
    {
        var drafts = await _db.StatementDrafts
            .Include(d => d.Entries)
            .Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft)
            .OrderByDescending(d => d.CreatedUtc)
            .ToListAsync(ct);
        return drafts.Select(Map).ToList();
    }

    public async Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        _db.Entry(draft.AddEntry(bookingDate, amount, subject)).State = EntityState.Added;
        await ClassifyInternalAsync(draft, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return Map(draft);
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
                e.IsAnnounced));
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
        return Map(draft);
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
        return Map(draft);
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
        return Map(draft);
    }

    private async Task ClassifyInternalAsync(StatementDraft draft, Guid ownerUserId, CancellationToken ct)
    {
        // Account detection fallback if still not set: pick single account if only one exists
        if (draft.DetectedAccountId == null)
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

        // Preload data needed for classification
        var contacts = await _db.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);
        var aliasLookup = await _db.AliasNames.AsNoTracking()
            .Where(a => contacts.Select(c => c.Id).Contains(a.ContactId))
            .GroupBy(a => a.ContactId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Pattern).ToList(), ct);

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

            // Contact resolution: match by exact name or alias contains in subject/counterparty
            var normalizedSubject = (entry.Subject ?? string.Empty).ToLowerInvariant();
            var normalizedRecipient = (entry.RecipientName ?? string.Empty).ToLowerInvariant();

            Guid? matchedContactId = contacts
                .Where(c => string.Equals(c.Name, entry.RecipientName, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Name, entry.Subject, StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Id)
                .FirstOrDefault();
            if (matchedContactId == Guid.Empty)
            {
                // alias match
                foreach (var kvp in aliasLookup)
                {
                    if (kvp.Value.Any(p => (!string.IsNullOrWhiteSpace(p)) && (normalizedSubject.Contains(p.ToLowerInvariant()) || normalizedRecipient.Contains(p.ToLowerInvariant()))))
                    {
                        matchedContactId = kvp.Key;
                        break;
                    }
                }
            }

            // Fallback: Wenn kein Empfängername angegeben ist, setze Bankkontakt als Empfänger
            if (string.IsNullOrWhiteSpace(entry.RecipientName) && bankContactId != null && bankContactId != Guid.Empty)
            {
                entry.MarkAccounted(bankContactId.Value);
            }
            else if (matchedContactId != null && matchedContactId != Guid.Empty)
            {
                entry.MarkAccounted(matchedContactId.Value);
            }
        }
    }

    private static StatementDraftDto Map(StatementDraft draft) => new(
        draft.Id,
        draft.OriginalFileName,
        draft.DetectedAccountId,
        draft.Status,
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
            e.Status,
            e.ContactId)).ToList());
}
