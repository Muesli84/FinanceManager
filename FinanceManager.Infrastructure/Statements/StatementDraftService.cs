using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Reader;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Securities;

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

    public async Task<StatementDraftEntryDto?> SaveEntryAllAsync(
        Guid draftId,
        Guid entryId,
        Guid ownerUserId,
        Guid? contactId,
        bool? isCostNeutral,
        Guid? savingsPlanId,
        bool? archiveOnBooking,
        Guid? securityId,
        SecurityTransactionType? transactionType,
        decimal? quantity,
        decimal? feeAmount,
        decimal? taxAmount,
        CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        if (draft.Status != StatementDraftStatus.Draft) { return null; }

        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }

        // Contact
        if (contactId == null)
        {
            entry.ClearContact();
        }
        else
        {
            bool exists = await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == contactId && c.OwnerUserId == ownerUserId, ct);
            if (!exists) { return null; }
            entry.MarkAccounted(contactId.Value);
        }

        // Cost neutral
        if (isCostNeutral.HasValue)
        {
            entry.MarkCostNeutral(isCostNeutral.Value);
        }

        // Savings plan
        if (entry.SavingsPlanId != savingsPlanId)
        {
            entry.AssignSavingsPlan(savingsPlanId);
        }
        if (archiveOnBooking.HasValue)
        {
            entry.SetArchiveSavingsPlanOnBooking(archiveOnBooking.Value);
        }

        // Security
        if (securityId != entry.SecurityId
            || transactionType != entry.SecurityTransactionType
            || quantity != entry.SecurityQuantity
            || feeAmount != entry.SecurityFeeAmount
            || taxAmount != entry.SecurityTaxAmount)
        {
            entry.SetSecurity(securityId, transactionType, quantity, feeAmount, taxAmount);
        }

        await _db.SaveChangesAsync(ct);

        // Re-evaluate parent statuses if this draft is parent or child in a split
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
            entry.ArchiveSavingsPlanOnBooking,
            entry.SplitDraftId,
            entry.SecurityId,
            entry.SecurityTransactionType,
            entry.SecurityQuantity,
            entry.SecurityFeeAmount,
            entry.SecurityTaxAmount);
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
        var batchCounter = 1;
        foreach (var movement in parsedDraft.Movements)
        {
            if (draft.Entries.Count == 100)
            {
                draft.Description += $" ({batchCounter++})";
                yield return await FinishDraftAsync(draft, ownerUserId, ct);
                draft = await CreateDraftHeader(ownerUserId, originalFileName, fileBytes, parsedDraft, ct);
            }
            var contact = _db.Contacts.AsNoTracking()
                .FirstOrDefault(c => c.OwnerUserId == ownerUserId && c.Id == movement.ContactId);
            draft.AddEntry(movement.BookingDate, movement.Amount, movement.Subject ?? string.Empty, contact?.Name ?? movement.Counterparty, movement.ValutaDate, movement.CurrencyCode, movement.PostingDescription, movement.IsPreview, false);
        }
        if (batchCounter > 1)
            draft.Description += $" ({batchCounter++})";
        yield return await FinishDraftAsync(draft, ownerUserId, ct);
    }

    private async Task<StatementDraft> CreateDraftHeader(Guid ownerUserId, string originalFileName, byte[] fileBytes, StatementParseResult parsedDraft, CancellationToken ct)
    {
        var draft = new StatementDraft(ownerUserId, originalFileName, parsedDraft.Header.AccountNumber, parsedDraft.Header.Description);
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
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Description)
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
        if (draftEntry is null)
            return null;
        return Map(draftEntry);
    }

    public async Task<StatementDraftDto?> AddEntryAsync(Guid draftId, Guid ownerUserId, DateTime bookingDate, decimal amount, string subject, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        var entry = _db.Entry(draft.AddEntry(bookingDate, amount, subject));
        entry.State = EntityState.Added;
        await _db.SaveChangesAsync(ct);
        await ClassifyInternalAsync(draft, entry.Entity.Id, ownerUserId, ct);
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

    public async Task<StatementDraftDto?> ClassifyAsync(Guid? draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        var drafts = await _db.StatementDrafts
            .Where(d => d.Status != StatementDraftStatus.Committed)
            .Where(d => d.OwnerUserId == ownerUserId && (draftId == null || d.Id == draftId))
            .ToListAsync(ct);
        if (!drafts.Any()) { return null; }
        List<StatementDraftDto> resultLust = new List<StatementDraftDto>();
        foreach (var draft in drafts)
        {
            await ClassifyInternalAsync(draft, entryId, ownerUserId, ct);
            await _db.SaveChangesAsync(ct);
            resultLust.Add(await GetDraftAsync(draft.Id, ownerUserId, ct));
        }
        return resultLust.FirstOrDefault();
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
            entry.ArchiveSavingsPlanOnBooking,
            entry.SplitDraftId,
            entry.SecurityId,
            entry.SecurityTransactionType,
            entry.SecurityQuantity,
            entry.SecurityFeeAmount,
            entry.SecurityTaxAmount);
    }

    public async Task<StatementDraftDto?> SetEntryArchiveSavingsPlanOnBookingAsync(Guid draftId, Guid entryId, bool archive, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return null; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }
        entry.SetArchiveSavingsPlanOnBooking(archive);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draftId, ownerUserId, ct);
    }

    // Restore interface methods grouped here
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
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == userId, ct);
        if (draft == null) { return null; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return null; }
        entry.SetSecurity(securityId, transactionType, quantity, feeAmount, taxAmount);
        await _db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task<DraftValidationResultDto> ValidateAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        var messages = new List<DraftValidationMessageDto>();
        if (draft == null)
        {
            return new DraftValidationResultDto(draftId, false, messages);
        }
        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || entryId == e.Id)).ToArrayAsync();

        void Add(string code, string sev, string msg, Guid? eId) => messages.Add(new DraftValidationMessageDto(code, sev, msg, draft.Id, eId));

        if (draft.DetectedAccountId == null)
        {
            Add("NO_ACCOUNT", "Error", "Kein Konto zugeordnet.", null);
        }

        var self = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);
        var account = draft.DetectedAccountId == null
            ? null
            : await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, ct);

        // Split cycle detection helper
        var dfsVisited = new HashSet<Guid>();
        var dfsStack = new HashSet<Guid>();
        bool DetectCycle(Guid dId)
        {
            if (!dfsVisited.Add(dId)) { return false; }
            dfsStack.Add(dId);
            var nextIds = _db.StatementDraftEntries.AsNoTracking().Where(e => e.DraftId == dId && e.SplitDraftId != null).Select(e => e.SplitDraftId!.Value).ToList();
            foreach (var nid in nextIds)
            {
                if (dfsStack.Contains(nid)) { return true; }
                if (!dfsVisited.Contains(nid) && DetectCycle(nid)) { return true; }
            }
            dfsStack.Remove(dId);
            return false;
        }
        if (DetectCycle(draft.Id))
        {
            Add("SPLIT_CYCLE_DETECTED", "Error", "[Split] Zyklen in Split-Verknüpfungen erkannt.", null);
        }

        async Task ValidateSplitChainAsync(StatementDraftEntry parentEntry, string prefix, CancellationToken token)
        {
            if (parentEntry.SplitDraftId == null) { return; }
            var childDraft = await _db.StatementDrafts.Include(d => d.Entries).FirstOrDefaultAsync(d => d.Id == parentEntry.SplitDraftId && d.OwnerUserId == ownerUserId, token);
            if (childDraft == null) { return; }
            if (childDraft.DetectedAccountId != null)
            {
                messages.Add(new DraftValidationMessageDto("SPLIT_DRAFT_HAS_ACCOUNT", "Error", prefix + "Split-Draft darf kein Konto zugeordnet haben.", draft.Id, parentEntry.Id));
            }
            var childSum = childDraft.Entries.Sum(x => x.Amount);
            if (childSum != parentEntry.Amount)
            {
                messages.Add(new DraftValidationMessageDto("SPLIT_AMOUNT_MISMATCH", "Error", prefix + "Summe der Aufteilung entspricht nicht dem Ursprungsbetrag.", draft.Id, parentEntry.Id));
            }

            foreach (var ce in entries)
            {
                if (ce.ContactId == null)
                {
                    messages.Add(new DraftValidationMessageDto("ENTRY_NO_CONTACT", "Error", prefix + "Kein Kontakt zugeordnet.", draft.Id, ce.Id));
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, token);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (ce.SplitDraftId == null)
                    {
                        messages.Add(new DraftValidationMessageDto("INTERMEDIARY_NO_SPLIT", "Error", prefix + "Zahlungsdienst ohne weitere Aufteilung.", draft.Id, ce.Id));
                    }
                    else
                    {
                        await ValidateSplitChainAsync(ce, prefix, token);
                    }
                }
                if (self != null && ce.ContactId == self.Id && ce.SavingsPlanId == null)
                {
                    messages.Add(new DraftValidationMessageDto("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", prefix + "Für Eigentransfer ist ein Sparplan sinnvoll.", draft.Id, ce.Id));
                }

                // Security validations for split entries as well
                if (ce.SecurityId != null && account != null)
                {
                    if (ce.ContactId != account.BankContactId)
                    {
                        messages.Add(new DraftValidationMessageDto("SECURITY_INVALID_CONTACT", "Error", prefix + "Wertpapierbuchung erfordert Bankkontakt des Kontos.", draft.Id, ce.Id));
                    }
                    if (ce.SecurityTransactionType == null)
                    {
                        messages.Add(new DraftValidationMessageDto("SECURITY_MISSING_TXTYPE", "Error", prefix + "Wertpapier: Transaktionstyp fehlt.", draft.Id, ce.Id));
                    }
                    if (ce.SecurityTransactionType != null && ce.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (ce.SecurityQuantity == null || ce.SecurityQuantity <= 0m)
                        {
                            messages.Add(new DraftValidationMessageDto("SECURITY_MISSING_QUANTITY", "Error", prefix + "Wertpapier: Stückzahl fehlt.", draft.Id, ce.Id));
                        }
                    }
                    var fee = ce.SecurityFeeAmount ?? 0m;
                    var tax = ce.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(ce.Amount))
                    {
                        messages.Add(new DraftValidationMessageDto("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", prefix + "Wertpapier: Gebühren+Steuern übersteigen Betrag.", draft.Id, ce.Id));
                    }
                }
            }
        }

        foreach (var e in entries)
        {
            if (entryId != null && e.Id != entryId) { continue; }

            if (e.ContactId == null)
            {
                Add("ENTRY_NO_CONTACT", "Error", "Kein Kontakt zugeordnet.", e.Id);
                continue;
            }
            else if (e.Status == StatementDraftEntryStatus.Open)
            {
                Add("ENTRY_NEEDS_CHECK", "Error", "Eine Prüfung der Angaben ist erforderlich.", e.Id);
                continue;
            }

            var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == e.ContactId && c.OwnerUserId == ownerUserId, ct);
            if (contact == null) { continue; }

            if (contact.IsPaymentIntermediary)
            {
                if (e.SplitDraftId == null)
                {
                    Add("INTERMEDIARY_NO_SPLIT", "Error", "Zahlungsdienst ohne Aufteilungs-Entwurf.", e.Id);
                }
                else
                {
                    await ValidateSplitChainAsync(e, "[Split] ", ct);
                }
            }

            if (self != null && e.ContactId == self.Id)
            {
                if (e.SavingsPlanId == null)
                {
                    Add("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", "Für Eigentransfer ist ein Sparplan sinnvoll.", e.Id);
                }
                else if (account != null && account.Type == AccountType.Savings)
                {
                    Add("SAVINGSPLAN_INVALID_ACCOUNT", "Error", "Sparplan auf Sparkonto ist nicht zulässig.", e.Id);
                }
            }

            // Security validations
            if (e.SecurityId != null && account != null)
            {
                if (e.ContactId != account.BankContactId)
                {
                    Add("SECURITY_INVALID_CONTACT", "Error", "Wertpapierbuchung erfordert Bankkontakt des Kontos.", e.Id);
                }
                if (e.SecurityTransactionType == null)
                {
                    Add("SECURITY_MISSING_TXTYPE", "Error", "Wertpapier: Transaktionstyp fehlt.", e.Id);
                }
                if (e.SecurityTransactionType != null && e.SecurityTransactionType != SecurityTransactionType.Dividend)
                {
                    if (e.SecurityQuantity == null || e.SecurityQuantity <= 0m)
                    {
                        Add("SECURITY_MISSING_QUANTITY", "Error", "Wertpapier: Stückzahl fehlt.", e.Id);
                    }
                }
                var fee = e.SecurityFeeAmount ?? 0m;
                var tax = e.SecurityTaxAmount ?? 0m;
                if (fee + tax > Math.Abs(e.Amount))
                {
                    Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Wertpapier: Gebühren+Steuern übersteigen Betrag.", e.Id);
                }
            }
        }

        // Savings plan goal info/warning + archive-intent validation
        var planIds = entries.Where(e => e.SavingsPlanId != null).Select(e => e.SavingsPlanId!.Value).Distinct().ToList();
        if (planIds.Count > 0)
        {
            var plans = await _db.SavingsPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToListAsync(ct);
            var plannedByPlan = entries
                .Where(e => e.SavingsPlanId != null)
                .GroupBy(e => e.SavingsPlanId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => -x.Amount));

            foreach (var plan in plans)
            {
                bool wantsArchive = entries.Any(e => e.SavingsPlanId == plan.Id && e.ArchiveSavingsPlanOnBooking);
                if (plan.TargetAmount is not decimal target) { continue; }

                var current = await _db.Postings.AsNoTracking()
                    .Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

                var planned = plannedByPlan.TryGetValue(plan.Id, out var val) ? val : 0m;
                var remaining = target - current;

                if (remaining > 0m && planned == remaining)
                {
                    messages.Add(new("SAVINGSPLAN_GOAL_REACHED_INFO", "Information", $"Mit den Buchungen in diesem Auszug wird das Sparziel des Sparplans '{plan.Name}' erreicht.", draft.Id, null));
                }
                else if (remaining > 0m && planned > remaining)
                {
                    messages.Add(new("SAVINGSPLAN_GOAL_EXCEEDS", "Warning", $"Die geplanten Buchungen überschreiten das Sparziel des Sparplans '{plan.Name}'.", draft.Id, null));
                }

                if (wantsArchive)
                {
                    if (current + planned != target)
                    {
                        messages.Add(new("SAVINGSPLAN_ARCHIVE_MISMATCH", "Error", $"Sparplan '{plan.Name}' kann nicht archiviert werden: Buchungen gleichen den Restbetrag nicht exakt aus.", draft.Id, null));
                    }
                }
            }
        }

        // Savings plan due information (only when validating whole draft)
        if (entries.Count() > 0 && entryId is null)
        {
            DateTime latestBookingDate = draft.Entries.Max(e => e.BookingDate).Date;
            var monthStart = new DateTime(latestBookingDate.Year, latestBookingDate.Month, 1);
            var nextMonth = monthStart.AddMonths(1);

            static DateTime AdjustDueDate(DateTime d)
            {
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
                if (effectiveDue > latestBookingDate) { continue; }

                var currentAmount = await _db.Postings.AsNoTracking()
                    .Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                var target = plan.TargetAmount!.Value;
                if (currentAmount >= target) { continue; }

                var hasPostingThisMonth = await _db.Postings.AsNoTracking()
                    .AnyAsync(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan && p.BookingDate >= monthStart && p.BookingDate < nextMonth, ct);
                if (hasPostingThisMonth) { continue; }

                var assignedInOpenDraft = await _db.StatementDraftEntries
                    .Join(_db.StatementDrafts, e => e.DraftId, d => d.Id, (e, d) => new { e, d })
                    .AnyAsync(x => x.d.OwnerUserId == ownerUserId && x.d.Status == StatementDraftStatus.Draft && x.e.SavingsPlanId == plan.Id, ct);
                if (assignedInOpenDraft) { continue; }

                messages.Add(new("SAVINGSPLAN_DUE", "Information", $"Sparplan '{plan.Name}' ist fällig (Fälligkeitsdatum: {effectiveDue:d}).", draft.Id, null));
            }
        }

        var isValid = messages.All(m => !string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        return new DraftValidationResultDto(draft.Id, isValid, messages);
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

    public async Task<BookingResult> BookAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        // Validate scope: whole draft or single entry
        var validation = await ValidateAsync(draftId, entryId, ownerUserId, ct);
        var hasErrors = validation.Messages.Any(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var hasWarnings = validation.Messages.Any(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase));
        if (hasErrors)
        {
            return new BookingResult(false, hasWarnings, validation, null, null, null);
        }
        if (hasWarnings && !forceWarnings)
        {
            return new BookingResult(false, true, validation, null, null, null);
        }

        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.DetectedAccountId == null)
        {
            return new BookingResult(false, false, validation, null, null, null);
        }
        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || entryId == e.Id)).ToListAsync();

        // Prevent booking a split-child draft directly (for both whole and single-entry booking)
        var isChild = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SplitDraftId == draft.Id, ct);
        if (isChild)
        {
            return new BookingResult(false, hasWarnings, validation, null, null, null);
        }

        var account = await _db.Accounts.FirstAsync(a => a.Id == draft.DetectedAccountId, ct);
        var self = await _db.Contacts.FirstAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);

        var updatedPlanIds = new HashSet<Guid>();
        async Task UpdateRecurringPlanIfDueAsync(Guid planId, DateTime bookingDate, CancellationToken token)
        {
            if (updatedPlanIds.Contains(planId)) { return; }
            var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == planId, token);
            if (plan == null) { return; }
            if (plan.Type != SavingsPlanType.Recurring) { return; }
            if (plan.TargetDate == null || plan.Interval == null) { return; }
            var targetDate = plan.TargetDate.Value.Date;
            if (targetDate > bookingDate.Date) { return; }
            var months = GetMonthsToAdd(plan.Interval.Value);
            if (months <= 0) { return; }
            // extend until after booking date
            while (plan.TargetDate!.Value.Date <= bookingDate.Date)
            {
                plan.SetTarget(plan.TargetAmount, plan.TargetDate.Value.AddMonths(months));
            }
            updatedPlanIds.Add(plan.Id);
        }

        async Task<Guid> CreateBankAndContactAsync(StatementDraftEntry e, decimal amount, Guid? contactId, CancellationToken token)
        {
            var groupId = Guid.NewGuid();
            var bankPosting = new Domain.Postings.Posting(
                e.Id,
                PostingKind.Bank,
                account.Id,
                null,
                null,
                null,
                e.BookingDate,
                amount,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                null).SetGroup(groupId);
            _db.Postings.Add(bankPosting);
            await UpsertAggregatesAsync(bankPosting, token);

            var contactPosting = new Domain.Postings.Posting(
                e.Id,
                PostingKind.Contact,
                null,
                contactId,
                null,
                null,
                e.BookingDate,
                amount,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                null).SetGroup(groupId);
            _db.Postings.Add(contactPosting);
            await UpsertAggregatesAsync(contactPosting, token);

            return groupId;
        }

        async Task CreateSecurityPostingsAsync(StatementDraftEntry e, Guid groupId, CancellationToken token)
        {
            if (e.SecurityId == null) { return; }
            var fee = Math.Abs(e.SecurityFeeAmount ?? 0m);
            var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
            if (e.Amount < 0)
            {
                fee = -fee;
                tax = -tax;
            }
            var feeTaxFactor = e.SecurityTransactionType switch
            {
                SecurityTransactionType.Buy => 1,
                SecurityTransactionType.Sell => -1,
                SecurityTransactionType.Dividend => -1,
                _ => -1
            };
            var tradeAmount = e.SecurityTransactionType switch
            {
                SecurityTransactionType.Buy => e.Amount - fee - tax,
                SecurityTransactionType.Sell => e.Amount + fee + tax,
                SecurityTransactionType.Dividend => e.Amount + fee + tax,
                _ => e.Amount + fee + tax
            };

            var tradeSubType = e.SecurityTransactionType switch
            {
                SecurityTransactionType.Buy => SecurityPostingSubType.Buy,
                SecurityTransactionType.Sell => SecurityPostingSubType.Sell,
                SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend,
                _ => SecurityPostingSubType.Buy
            };

            var main = new Domain.Postings.Posting(
                e.Id,
                PostingKind.Security,
                null,
                null,
                null,
                e.SecurityId,
                e.BookingDate,
                tradeAmount,
                e.Subject,
                e.RecipientName,
                e.BookingDescription,
                tradeSubType).SetGroup(groupId);
            _db.Postings.Add(main);
            await UpsertAggregatesAsync(main, token);

            if (fee != 0m)
            {
                var feeP = new Domain.Postings.Posting(
                    e.Id,
                    PostingKind.Security,
                    null,
                    null,
                    null,
                    e.SecurityId,
                    e.BookingDate,
                    feeTaxFactor * fee,
                    e.Subject,
                    e.RecipientName,
                    e.BookingDescription,
                    SecurityPostingSubType.Fee).SetGroup(groupId);
                _db.Postings.Add(feeP);
                await UpsertAggregatesAsync(feeP, token);
            }
            if (tax != 0m)
            {
                var taxP = new Domain.Postings.Posting(
                    e.Id,
                    PostingKind.Security,
                    null,
                    null,
                    null,
                    e.SecurityId,
                    e.BookingDate,
                    feeTaxFactor * tax,
                    e.Subject,
                    e.RecipientName,
                    e.BookingDescription,
                    SecurityPostingSubType.Tax).SetGroup(groupId);
                _db.Postings.Add(taxP);
                await UpsertAggregatesAsync(taxP, token);
            }
        }

        async Task BookSplitDraftRecursiveAsync(Guid splitId, CancellationToken token, HashSet<Guid> visited)
        {
            if (!visited.Add(splitId)) { return; }
            var cd = await _db.StatementDrafts.Include(d => d.Entries).FirstOrDefaultAsync(d => d.Id == splitId && d.OwnerUserId == ownerUserId, token);
            if (cd == null) { return; }

            foreach (var ce in cd.Entries)
            {
                if (ce.ContactId == null) { continue; }
                var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, token);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary && ce.SplitDraftId != null)
                {
                    var gid = await CreateBankAndContactAsync(ce, 0m, ce.ContactId, token);
                    await CreateSecurityPostingsAsync(ce, gid, token);
                    await BookSplitDraftRecursiveAsync(ce.SplitDraftId.Value, token, visited);
                }
                else
                {
                    var gid = await CreateBankAndContactAsync(ce, ce.Amount, ce.ContactId, token);
                    if (ce.SavingsPlanId != null && self != null && ce.ContactId == self.Id)
                    {
                        var spPosting = new Domain.Postings.Posting(
                            ce.Id,
                            PostingKind.SavingsPlan,
                            null,
                            null,
                            ce.SavingsPlanId,
                            null,
                            ce.BookingDate,
                            -ce.Amount,
                            ce.Subject,
                            ce.RecipientName,
                            ce.BookingDescription,
                            null).SetGroup(gid);
                        _db.Postings.Add(spPosting);
                        await UpsertAggregatesAsync(spPosting, token);
                        await UpdateRecurringPlanIfDueAsync(ce.SavingsPlanId.Value, ce.BookingDate, token);
                    }
                    await CreateSecurityPostingsAsync(ce, gid, token);
                }
            }

            cd.MarkCommitted();
        }

        // Decide which entries to book
        List<StatementDraftEntry> toBook;
        if (entryId is null)
        {
            toBook = entries.ToList();
        }
        else
        {
            var e = entries.FirstOrDefault(x => x.Id == entryId.Value);
            if (e == null)
            {
                return new BookingResult(false, false, validation, null, null, await GetNextStatementDraftAsync(draft));
            }
            toBook = new List<StatementDraftEntry> { e };
        }

        foreach (var e in toBook)
        {
            if (e.ContactId == null) { continue; }
            var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == e.ContactId && c.OwnerUserId == ownerUserId, ct);
            if (contact == null) { continue; }

            if (contact.IsPaymentIntermediary && e.SplitDraftId != null)
            {
                var gid = await CreateBankAndContactAsync(e, 0m, e.ContactId, ct);
                await CreateSecurityPostingsAsync(e, gid, ct);
                await BookSplitDraftRecursiveAsync(e.SplitDraftId.Value, ct, new HashSet<Guid>());
            }
            else
            {
                var gid = await CreateBankAndContactAsync(e, e.Amount, e.ContactId, ct);
                if (e.SavingsPlanId != null && e.ContactId == self.Id)
                {
                    var spPosting = new Domain.Postings.Posting(
                        e.Id,
                        PostingKind.SavingsPlan,
                        null,
                        null,
                        e.SavingsPlanId,
                        null,
                        e.BookingDate,
                        -e.Amount,
                        e.Subject,
                        e.RecipientName,
                        e.BookingDescription,
                        null).SetGroup(gid);
                    _db.Postings.Add(spPosting);
                    await UpsertAggregatesAsync(spPosting, ct);
                    await UpdateRecurringPlanIfDueAsync(e.SavingsPlanId.Value, e.BookingDate, ct);
                }
                await CreateSecurityPostingsAsync(e, gid, ct);
            }
        }

        // Remove booked entries from draft (so they no longer appear)
        if (entryId is null)
        {
            // booking entire draft → mark committed after postings
            draft.MarkCommitted();
        }
        else
        {
            foreach (var e in toBook)
            {
                _db.StatementDraftEntries.Remove(e);
            }
            await _db.SaveChangesAsync(ct);

            if (!_db.StatementDraftEntries.Where(e => e.DraftId == draft.Id).Any())
            {
                draft.MarkCommitted();
            }
        }

        await _db.SaveChangesAsync(ct);

        // Archive savings plans if flagged on the booked entries and totals meet target after booking; append info to validation messages
        var flaggedPlanIds = toBook
            .Where(e => e.SavingsPlanId != null && e.ArchiveSavingsPlanOnBooking)
            .Select(e => e.SavingsPlanId!.Value)
            .Distinct()
            .ToList();
        if (flaggedPlanIds.Count > 0)
        {
            var msgs = validation.Messages.ToList();
            foreach (var pid in flaggedPlanIds)
            {
                var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == pid, ct);
                if (plan == null || !plan.IsActive) { continue; }
                var target = plan.TargetAmount ?? 0m;
                var current = await _db.Postings.AsNoTracking()
                    .Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan)
                    .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                if (current == target)
                {
                    plan.Archive();
                    msgs.Add(new("SAVINGSPLAN_ARCHIVED", "Information", $"Sparplan '{plan.Name}' wurde archiviert.", draft.Id, null));
                }
            }
            await _db.SaveChangesAsync(ct);
            validation = new DraftValidationResultDto(validation.DraftId, validation.IsValid, msgs);
        }

        var totalBooked = toBook.Count;
        return new BookingResult(true, false, validation, null, totalBooked, await GetNextStatementDraftAsync(draft));
    }
    private async Task<Guid?> GetNextStatementDraftAsync(StatementDraft draft)
    {
        var nextDraft = await _db.StatementDrafts.OrderBy(d => d.Id).Where(d => d.Status == StatementDraftStatus.Draft).Where(d => d.Id > draft.Id).FirstOrDefaultAsync();
        if (nextDraft is null)
                nextDraft = await _db.StatementDrafts.OrderBy(d => d.Id).Where(d => d.Status == StatementDraftStatus.Draft).LastOrDefaultAsync();
        return nextDraft?.Id;
    }
}
