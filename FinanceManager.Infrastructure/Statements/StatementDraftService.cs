using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements;
using FinanceManager.Domain.Accounts; // added for Account type
using FinanceManager.Infrastructure.Migrations;
using FinanceManager.Infrastructure.Statements.Reader;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Application.Aggregates;
using Microsoft.Extensions.Logging; // added
using Microsoft.Extensions.Logging.Abstractions; // added

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService : IStatementDraftService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileReader> _statementFileReaders;
    private readonly IPostingAggregateService _aggregateService;
    private readonly ILogger<StatementDraftService> _logger; // added
    private List<StatementDraftDto> allDrafts = null;
    private List<Security> allSecurities = null;
    private List<Domain.Accounts.Account> allAccounts = null;

    public ImportSplitInfo? LastImportSplitInfo { get; private set; } // exposes metadata of last CreateDraftAsync call (scoped service)

    public sealed record ImportSplitInfo(
        ImportSplitMode ConfiguredMode,
        bool EffectiveMonthly,
        int DraftCount,
        int TotalMovements,
        int MaxEntriesPerDraft,
        int LargestDraftSize,
        int MonthlyThreshold
    );

    public StatementDraftService(AppDbContext db, IPostingAggregateService aggregateService, IEnumerable<IStatementFileReader>? readers = null, ILogger<StatementDraftService>? logger = null) // logger param added
    {
        _db = db;
        _aggregateService = aggregateService;
        _logger = logger ?? NullLogger<StatementDraftService>.Instance; 
        _statementFileReaders = (readers is not null && readers.ToList().Any()) ? readers.ToList(): new List<IStatementFileReader>
        {
            new ING_PDfReader(),
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

    // updated to also set fees from parsed details
    public async Task AddStatementDetailsAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct)
    {
        var parsedDraft = _statementFileReaders
            .Select(reader => reader.ParseDetails(originalFileName, fileBytes))
            .Where(result => result is not null && result.Movements.Any())
            .FirstOrDefault();
        if (parsedDraft is null)
        {
            throw new InvalidOperationException("No valid statement file reader found or no movements detected.");
        }

        if (allAccounts is null)
            allAccounts = await _db.Accounts.Where(a => a.OwnerUserId == ownerUserId).ToListAsync(ct);
        if (allDrafts is null)
            allDrafts = (await GetOpenDraftsAsync(ownerUserId, ct)).ToList();
        if (allSecurities is null)
            allSecurities = (await _db.Securities.Where(s => s.IsActive).ToListAsync(ct)).ToList();

        var account = allAccounts.FirstOrDefault(acc => acc.Iban == parsedDraft.Header.IBAN);
        var line = parsedDraft.Movements.FirstOrDefault();
        var security = allSecurities.FirstOrDefault(s => s.IsActive && line.Subject.Contains(s.Identifier, StringComparison.OrdinalIgnoreCase));
        if (security is not null)
            foreach (var draft in allDrafts.Where(draft => draft.DetectedAccountId == account.Id))
            {
                var draftEntries = await _db.StatementDraftEntries
                    .Where(e => e.DraftId == draft.DraftId)
                    .Where(e => e.ContactId == account.BankContactId)
                    .Where(e => e.SecurityId == security.Id)
                    .Where(e => e.Amount == line.Amount)
                    .Where(e => e.BookingDate == line.BookingDate)
                    .ToListAsync(ct);
                if (!draftEntries.Any()) continue;
                var entries = draftEntries.ToList();
                if (entries.Count == 0) continue;

                if (entries.Count > 1) continue;
                var entry = entries.FirstOrDefault();
                entry.SetSecurity(entry.SecurityId, line.PostingDescription switch
                {
                    "Dividend" => SecurityTransactionType.Dividend,
                    "Sell" => SecurityTransactionType.Sell,
                    "Buy" => SecurityTransactionType.Buy,
                    _ => entry.SecurityTransactionType
                }, line.PostingDescription switch {
                    "Dividend" => null,
                    "Sell" => line.Quantity ?? entry.SecurityQuantity,
                    "Buy" => line.Quantity ?? entry.SecurityQuantity,
                    _ => entry.SecurityQuantity
                }, line.FeeAmount ?? entry.SecurityFeeAmount, line.TaxAmount ?? entry.SecurityTaxAmount);
                await _db.SaveChangesAsync();
                break;
            }

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
        // Generate one UploadGroupId for all drafts originating from this upload
        var uploadGroupId = Guid.NewGuid();

        var userSettings = await _db.Users.AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => new
            {
                u.ImportSplitMode,
                u.ImportMaxEntriesPerDraft,
                u.ImportMonthlySplitThreshold
            })
            .SingleOrDefaultAsync(ct);
        var mode = userSettings?.ImportSplitMode ?? ImportSplitMode.MonthlyOrFixed;
        var maxPerDraft = userSettings?.ImportMaxEntriesPerDraft ?? 250;
        var monthlyThreshold = userSettings?.ImportMonthlySplitThreshold ?? maxPerDraft;
        var allMovements = parsedDraft.Movements
            .OrderBy(m => m.BookingDate)
            .ThenBy(m => m.Subject)
            .ToList();
        bool useMonthly = mode switch
        {
            ImportSplitMode.Monthly => true,
            ImportSplitMode.FixedSize => false,
            ImportSplitMode.MonthlyOrFixed => allMovements.Count > monthlyThreshold,
            _ => false
        };
        static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> source, int size)
        {
            if (size <= 0) { yield return source.ToList(); yield break; }
            for (int i = 0; i < source.Count; i += size)
            {
                yield return source.Skip(i).Take(size).ToList();
            }
        }
        IEnumerable<(string Label, List<StatementMovement> Movements)> EnumerateGroups()
        {
            if (useMonthly)
            {
                var groups = allMovements
                    .GroupBy(m => new { m.BookingDate.Year, m.BookingDate.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month);
                foreach (var g in groups)
                {
                    var monthLabel = $"{g.Key.Year:D4}-{g.Key.Month:D2}";
                    var parts = Chunk(g.ToList(), maxPerDraft).ToList();
                    if (parts.Count == 1)
                    {
                        yield return (monthLabel, parts[0]);
                    }
                    else
                    {
                        for (int p = 0; p < parts.Count; p++)
                        {
                            yield return ($"{monthLabel} (Teil {p + 1})", parts[p]);
                        }
                    }
                }
            }
            else
            {
                var chunks = Chunk(allMovements, maxPerDraft).ToList();
                if (chunks.Count == 1)
                {
                    yield return (string.Empty, chunks[0]);
                }
                else
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        yield return ($"(Teil {i + 1})", chunks[i]);
                    }
                }
            }
        }

        // Materialisieren für Logging (FA-AUSZ-016-06) + UI Hinweis (FA-AUSZ-016-07)
        var groupInfos = EnumerateGroups().ToList();
        var largest = groupInfos.Count == 0 ? 0 : groupInfos.Max(g => g.Movements.Count);
        LastImportSplitInfo = new ImportSplitInfo(mode, useMonthly, groupInfos.Count, allMovements.Count, maxPerDraft, largest, monthlyThreshold);
        _logger.LogInformation("ImportSplit result: Mode={Mode} EffectiveUseMonthly={UseMonthly} Movements={Movements} Drafts={DraftCount} MaxEntriesPerDraft={MaxPerDraft} LargestDraftSize={Largest} Threshold={Threshold} File={File}",
            mode, useMonthly, allMovements.Count, groupInfos.Count, maxPerDraft, largest, monthlyThreshold, originalFileName);
        var baseDescription = parsedDraft.Header.Description;
        foreach (var group in groupInfos)
        {
            ct.ThrowIfCancellationRequested();
            // Draft Header erstellen
            var draft = await CreateDraftHeader(ownerUserId, originalFileName, fileBytes, parsedDraft, ct);
            draft.SetUploadGroup(uploadGroupId);
            // Beschreibung anreichern (Monat / Teil)
            if (!string.IsNullOrWhiteSpace(group.Label))
            {
                draft.Description = string.IsNullOrWhiteSpace(baseDescription)
                    ? group.Label
                    : $"{baseDescription} {group.Label}";
            }

            // Einträge hinzufügen
            foreach (var movement in group.Movements)
            {
                var contact = _db.Contacts.AsNoTracking()
                    .FirstOrDefault(c => c.OwnerUserId == ownerUserId && c.Id == movement.ContactId);

                draft.AddEntry(
                    movement.BookingDate,
                    movement.Amount,
                    movement.Subject ?? string.Empty,
                    contact?.Name ?? movement.Counterparty,
                    movement.ValutaDate,
                    movement.CurrencyCode,
                    movement.PostingDescription,
                    movement.IsPreview,
                    false);
            }

            yield return await FinishDraftAsync(draft, ownerUserId, ct);
        }
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

    public Task<int> GetOpenDraftsCountAsync(Guid userId, CancellationToken token)
    {
       return _db.StatementDrafts
            .Include(d => d.Entries)
            .Where(d => d.OwnerUserId == userId && d.Status == StatementDraftStatus.Draft)            
            .AsNoTracking()
            .CountAsync();
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
            // NEW (Option A): Require different upload groups (must NOT come from same upload)
            if (draft.UploadGroupId != null && splitDraft.UploadGroupId != null && draft.UploadGroupId == splitDraft.UploadGroupId)
            {
                throw new InvalidOperationException("Split drafts must NOT originate from the same upload (UploadGroupId must differ).");
            }
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
        return await GetDraftAsync(draft.Id, ownerUserId, ct);
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
        var scopedEntryQuery = _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || entryId == e.Id));
        var entries = await scopedEntryQuery.ToArrayAsync(ct);

        void Add(string code, string sev, string msg, Guid? eId) => messages.Add(new DraftValidationMessageDto(code, sev, msg, draft.Id, eId));

        if (draft.DetectedAccountId == null)
        {
            Add("NO_ACCOUNT", "Error", "Kein Konto zugeordnet.", null);
        }

        var self = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);
        var account = draft.DetectedAccountId == null
            ? null
            : await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, ct);

        // Cycle detection per original implementation
        var visited = new HashSet<Guid>();
        var stack = new HashSet<Guid>();
        bool DetectCycle(Guid id)
        {
            if (!visited.Add(id)) { return false; }
            stack.Add(id);
            var nextIds = _db.StatementDraftEntries.AsNoTracking()
                .Where(e => e.DraftId == id && e.SplitDraftId != null)
                .Select(e => e.SplitDraftId!.Value)
                .ToList();
            foreach (var nid in nextIds)
            {
                if (stack.Contains(nid)) { return true; }
                if (DetectCycle(nid)) { return true; }
            }
            stack.Remove(id);
            return false;
        }
        if (DetectCycle(draft.Id))
        {
            Add("SPLIT_CYCLE_DETECTED", "Error", "[Split] Zyklen in Split-Verknüpfungen erkannt.", null);
        }

        async Task<List<StatementDraft>> LoadSplitGroupAsync(StatementDraft rootChild, StatementDraft parent, Guid userId, CancellationToken token)
        {
            if (rootChild.UploadGroupId == null)
            {
                return new List<StatementDraft> { rootChild };
            }
            var group = await _db.StatementDrafts
                .Include(d => d.Entries)
                .Where(d => d.OwnerUserId == userId && d.Status == StatementDraftStatus.Draft)
                .Where(d => d.UploadGroupId == rootChild.UploadGroupId)
                .Where(d => d.Id != parent.Id)
                .ToListAsync(token);
            if (!group.Any(g => g.Id == rootChild.Id)) { group.Add(rootChild); }
            return group;
        }

        async Task ValidateSplitBranchAsync(StatementDraftEntry parentEntry, string prefix, CancellationToken token)
        {
            if (parentEntry.SplitDraftId == null) { return; }
            var firstChild = await _db.StatementDrafts.Include(d => d.Entries)
                .FirstOrDefaultAsync(d => d.Id == parentEntry.SplitDraftId && d.OwnerUserId == ownerUserId, token);
            if (firstChild == null) { return; }
            var groupDrafts = await LoadSplitGroupAsync(firstChild, draft, ownerUserId, token);
            var groupEntries = groupDrafts.SelectMany(d => d.Entries).ToList();
            if (groupDrafts.Any(d => d.DetectedAccountId != null))
            {
                messages.Add(new("SPLIT_DRAFT_HAS_ACCOUNT", "Error", prefix + "Split-Draft darf kein Konto zugeordnet haben.", draft.Id, parentEntry.Id));
            }
            var sum = groupEntries.Sum(e => e.Amount);
            if (sum != parentEntry.Amount)
            {
                messages.Add(new("SPLIT_AMOUNT_MISMATCH", "Error", prefix + "Summe der Aufteilung entspricht nicht dem Ursprungsbetrag.", draft.Id, parentEntry.Id));
            }
            foreach (var ce in groupEntries)
            {
                if (ce.ContactId == null)
                {
                    messages.Add(new("ENTRY_NO_CONTACT", "Error", prefix + "Kein Kontakt zugeordnet.", draft.Id, ce.Id));
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, token);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (ce.SplitDraftId == null)
                    {
                        messages.Add(new("INTERMEDIARY_NO_SPLIT", "Error", prefix + "Zahlungsdienst ohne weitere Aufteilung.", draft.Id, ce.Id));
                    }
                    else
                    {
                        await ValidateSplitBranchAsync(ce, prefix, token); // deeper recursion
                    }
                }
                if (self != null && ce.ContactId == self.Id && ce.SavingsPlanId == null)
                {
                    messages.Add(new("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", prefix + "Für Eigentransfer ist ein Sparplan sinnvoll.", draft.Id, ce.Id));
                }
                // Security validations reused from original logic
                if (ce.SecurityId != null && account != null)
                {
                    if (ce.ContactId != account.BankContactId)
                    {
                        messages.Add(new("SECURITY_INVALID_CONTACT", "Error", prefix + "Wertpapierbuchung erfordert Bankkontakt des Kontos.", draft.Id, ce.Id));
                    }
                    if (ce.SecurityTransactionType == null)
                    {
                        messages.Add(new("SECURITY_MISSING_TXTYPE", "Error", prefix + "Wertpapier: Transaktionstyp fehlt.", draft.Id, ce.Id));
                    }
                    if (ce.SecurityTransactionType != null && ce.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (ce.SecurityQuantity == null || ce.SecurityQuantity <= 0m)
                        {
                            messages.Add(new("SECURITY_MISSING_QUANTITY", "Error", prefix + "Wertpapier: Stückzahl fehlt.", draft.Id, ce.Id));
                        }
                    }
                    if (ce.SecurityTransactionType == SecurityTransactionType.Dividend && ce.SecurityQuantity != null)
                    {
                        messages.Add(new("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", prefix + "Wertpapier: Menge ist bei Dividende nicht zulässig.", draft.Id, ce.Id));
                    }
                    var fee = ce.SecurityFeeAmount ?? 0m;
                    var tax = ce.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(ce.Amount))
                    {
                        messages.Add(new("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", prefix + "Wertpapier: Gebühren+Steuern übersteigen Betrag.", draft.Id, ce.Id));
                    }
                }
            }
        }

        foreach (var e in entries)
        {
            try
            {
                if (entryId != null && e.Id != entryId) { continue; }
                if (e.ContactId == null)
                {
                    Add("ENTRY_NO_CONTACT", "Error", "Kein Kontakt zugeordnet.", e.Id);
                    continue;
                }
                if (e.Status == StatementDraftEntryStatus.Open)
                {
                    Add("ENTRY_NEEDS_CHECK", "Error", "Eine Prüfung der Angaben ist erforderlich.", e.Id);
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.ContactId && x.OwnerUserId == ownerUserId, ct);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (e.SplitDraftId == null)
                    {
                        Add("INTERMEDIARY_NO_SPLIT", "Error", "Zahlungsdienst ohne Aufteilungs-Entwurf.", e.Id);
                    }
                    else
                    {
                        await ValidateSplitBranchAsync(e, "[Split] ", ct);
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
                    if (e.SecurityTransactionType == SecurityTransactionType.Dividend && e.SecurityQuantity != null)
                    {
                        Add("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", "Wertpapier: Menge ist bei Dividende nicht zulässig.", e.Id);
                    }
                    var fee = e.SecurityFeeAmount ?? 0m;
                    var tax = e.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(e.Amount))
                    {
                        Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Wertpapier: Gebühren+Steuern übersteigen Betrag.", e.Id);
                    }
                }
            }
            finally
            {
                if (messages.Any(m => m.EntryId == e.Id && m.Severity == "Error"))
                {
                    e.MarkNeedsCheck();
                }
            }
        }
        await _db.SaveChangesAsync(ct);

        // Reuse original savings plan validation + due info (unchanged) ------------------
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
                if (wantsArchive && current + planned != target)
                {
                    messages.Add(new("SAVINGSPLAN_ARCHIVE_MISMATCH", "Error", $"Sparplan '{plan.Name}' kann nicht archiviert werden: Buchungen gleichen den Restbetrag nicht exakt aus.", draft.Id, null));
                }
            }
        }
        if (entries.Length > 0 && entryId is null)
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

    public async Task<BookingResult> BookAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        var validation = await ValidateAsync(draftId, entryId, ownerUserId, ct);
        var hasErrors = validation.Messages.Any(m => m.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase));
        var hasWarnings = validation.Messages.Any(m => m.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase));
        if (hasErrors)
        {
            return new BookingResult(false, hasWarnings, validation, null, null, null);
        }
        if (hasWarnings && !forceWarnings)
        {
            return new BookingResult(false, true, validation, null, null, null);
        }

        var draft = await _db.StatementDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.DetectedAccountId == null)
        {
            return new BookingResult(false, false, validation, null, null, null);
        }

        // Disallow booking a child draft directly
        bool isChild = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SplitDraftId == draft.Id, ct);
        if (isChild)
        {
            return new BookingResult(false, hasWarnings, validation, null, null, null);
        }

        var account = await _db.Accounts.FirstAsync(a => a.Id == draft.DetectedAccountId, ct);
        var self = await _db.Contacts.FirstAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);
        var allEntriesScope = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || e.Id == entryId)).ToListAsync(ct);

        var updatedPlanIds = new HashSet<Guid>();
        async Task UpdateRecurringPlanIfDueAsync(Guid planId, DateTime bookingDate, CancellationToken token)
        {
            if (updatedPlanIds.Contains(planId)) { return; }
            var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == planId, token);
            if (plan == null) { return; }
            if (plan.Type != SavingsPlanType.Recurring || plan.TargetDate == null || plan.Interval == null) { return; }
            while (plan.TargetDate.Value.Date <= bookingDate.Date)
            {
                plan.SetTarget(plan.TargetAmount, plan.TargetDate.Value.AddMonths(GetMonthsToAdd(plan.Interval.Value)));
            }
            updatedPlanIds.Add(plan.Id);
        }

        async Task<Guid> CreateBankAndContactPostingAsync(StatementDraftEntry e, decimal amount, Guid? contactId, CancellationToken token)
        {
            var gid = Guid.NewGuid();
            var bankPosting = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
            _db.Postings.Add(bankPosting); await UpsertAggregatesAsync(bankPosting, token);
            var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
            _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, token);
            return gid;
        }
        async Task CreateSecurityPostingsAsync(StatementDraftEntry e, Guid gid, CancellationToken token)
        {
            if (e.SecurityId == null) { return; }
            var fee = Math.Abs(e.SecurityFeeAmount ?? 0m); var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
            if (e.Amount < 0) { fee = -fee; tax = -tax; }
            var factor = e.SecurityTransactionType switch { SecurityTransactionType.Buy => 1, SecurityTransactionType.Sell => -1, SecurityTransactionType.Dividend => -1, _ => -1 };
            var tradeAmount = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.Amount - fee - tax, SecurityTransactionType.Sell => e.Amount + fee + tax, SecurityTransactionType.Dividend => e.Amount + fee + tax, _ => e.Amount + fee + tax };
            var tradeSub = e.SecurityTransactionType switch { SecurityTransactionType.Buy => SecurityPostingSubType.Buy, SecurityTransactionType.Sell => SecurityPostingSubType.Sell, SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend, _ => SecurityPostingSubType.Buy };
            decimal? qty = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.SecurityQuantity.HasValue ? Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Sell => e.SecurityQuantity.HasValue ? -Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Dividend => null, _ => e.SecurityQuantity };
            var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tradeAmount, e.Subject, e.RecipientName, e.BookingDescription, tradeSub, qty).SetGroup(gid);
            _db.Postings.Add(main); await UpsertAggregatesAsync(main, token);
            if (fee != 0m)
            {
                var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid);
                _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, token);
            }
            if (tax != 0m)
            {
                var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid);
                _db.Postings.Add(taxP); await UpsertAggregatesAsync(taxP, token);
            }
        }

        var toBook = entryId == null ? allEntriesScope : allEntriesScope.Where(e => e.Id == entryId.Value).ToList();

        foreach (var e in toBook)
        {
            if (e.ContactId == null) { continue; }
            var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == e.ContactId && x.OwnerUserId == ownerUserId, ct);
            if (c == null) { continue; }
            if (c.IsPaymentIntermediary && e.SplitDraftId != null)
            {
                var gidZero = await CreateBankAndContactPostingAsync(e, 0m, e.ContactId, ct);
                await CreateSecurityPostingsAsync(e, gidZero, ct);
                await BookSplitDraftGroupAsync(e.SplitDraftId.Value, ownerUserId, draft.Id, self, account, ct, new HashSet<Guid>());
            }
            else
            {
                var gid = await CreateBankAndContactPostingAsync(e, e.Amount, e.ContactId, ct);
                if (e.SavingsPlanId != null && e.ContactId == self.Id)
                {
                    var spPosting = new Domain.Postings.Posting(e.Id, PostingKind.SavingsPlan, null, null, e.SavingsPlanId, null, e.BookingDate, -e.Amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(spPosting); await UpsertAggregatesAsync(spPosting, ct);
                    await UpdateRecurringPlanIfDueAsync(e.SavingsPlanId.Value, e.BookingDate, ct);
                }
                await CreateSecurityPostingsAsync(e, gid, ct);
            }
        }

        if (entryId == null)
        {
            draft.MarkCommitted();
        }
        else
        {
            foreach (var e in toBook)
            {
                _db.StatementDraftEntries.Remove(e);
            }
            await _db.SaveChangesAsync(ct);
            if (!await _db.StatementDraftEntries.AnyAsync(e => e.DraftId == draft.Id, ct))
            {
                draft.MarkCommitted();
            }
        }
        await _db.SaveChangesAsync(ct);

        // Archive savings plans if flagged and fully funded
        var flaggedPlans = toBook.Where(e => e.SavingsPlanId != null && e.ArchiveSavingsPlanOnBooking).Select(e => e.SavingsPlanId!.Value).Distinct().ToList();
        if (flaggedPlans.Count > 0)
        {
            var msgs = validation.Messages.ToList();
            foreach (var pid in flaggedPlans)
            {
                var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == pid, ct);
                if (plan == null || !plan.IsActive) { continue; }
                var target = plan.TargetAmount ?? 0m;
                var current = await _db.Postings.AsNoTracking().Where(p => p.SavingsPlanId == plan.Id && p.Kind == PostingKind.SavingsPlan).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                if (current == target)
                {
                    plan.Archive();
                    msgs.Add(new("SAVINGSPLAN_ARCHIVED", "Information", $"Sparplan '{plan.Name}' wurde archiviert.", draft.Id, null));
                }
            }
            await _db.SaveChangesAsync(ct);
            validation = new DraftValidationResultDto(validation.DraftId, validation.IsValid, msgs);
        }

        return new BookingResult(true, false, validation, null, toBook.Count, await GetNextStatementDraftAsync(draft));
    }

    private static int GetMonthsToAdd(SavingsPlanInterval interval) => interval switch
    {
        SavingsPlanInterval.Monthly => 1,
        SavingsPlanInterval.BiMonthly => 2,
        SavingsPlanInterval.Quarterly => 3,
        SavingsPlanInterval.SemiAnnually => 6,
        SavingsPlanInterval.Annually => 12,
        _ => 0
    };

    public async Task<bool> DeleteEntryAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries).FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return false; }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null) { return false; }
        _db.StatementDraftEntries.Remove(entry);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> DeleteAllAsync(Guid ownerUserId, CancellationToken ct)
    {
        var openIds = await _db.StatementDrafts.Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft).Select(d => d.Id).ToListAsync(ct);
        if (openIds.Count == 0) { return 0; }
        await _db.StatementDraftEntries.Where(e => openIds.Contains(e.DraftId)).ExecuteDeleteAsync(ct);
        var removed = await _db.StatementDrafts.Where(d => openIds.Contains(d.Id)).ExecuteDeleteAsync(ct);
        return removed;
    }

    private async Task BookSplitDraftGroupAsync(Guid representativeSplitDraftId, Guid ownerUserId, Guid parentDraftId, Contact self, Account account, CancellationToken ct, HashSet<Guid> visited)
    {
        if (!visited.Add(representativeSplitDraftId)) { return; }
        var rep = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == representativeSplitDraftId && d.OwnerUserId == ownerUserId, ct);
        if (rep == null) { return; }
        var group = rep.UploadGroupId == null
            ? new List<StatementDraft> { rep }
            : await _db.StatementDrafts.Include(d => d.Entries)
                .Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft && d.UploadGroupId == rep.UploadGroupId && d.Id != parentDraftId)
                .ToListAsync(ct);
        if (!group.Any(g => g.Id == rep.Id)) { group.Add(rep); }

        foreach (var childDraft in group)
        {
            foreach (var ce in childDraft.Entries)
            {
                if (ce.ContactId == null) { continue; }
                var contact = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, ct);
                if (contact == null) { continue; }

                async Task<Guid> CreateBankAndContactAsync(StatementDraftEntry e, decimal amount)
                {
                    var gid = Guid.NewGuid();
                    var bank = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(bank); await UpsertAggregatesAsync(bank, ct);
                    var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, ct);
                    return gid;
                }
                async Task CreateSecurityAsync(StatementDraftEntry e, Guid gid)
                {
                    if (e.SecurityId == null) return;
                    var fee = Math.Abs(e.SecurityFeeAmount ?? 0m); var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
                    if (e.Amount < 0) { fee = -fee; tax = -tax; }
                    var factor = e.SecurityTransactionType switch { SecurityTransactionType.Buy => 1, SecurityTransactionType.Sell => -1, SecurityTransactionType.Dividend => -1, _ => -1 };
                    var tradeAmt = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.Amount - fee - tax, SecurityTransactionType.Sell => e.Amount + fee + tax, SecurityTransactionType.Dividend => e.Amount + fee + tax, _ => e.Amount + fee + tax };
                    var sub = e.SecurityTransactionType switch { SecurityTransactionType.Buy => SecurityPostingSubType.Buy, SecurityTransactionType.Sell => SecurityPostingSubType.Sell, SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend, _ => SecurityPostingSubType.Buy };
                    decimal? qty = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.SecurityQuantity, SecurityTransactionType.Sell => e.SecurityQuantity.HasValue ? -Math.Abs(e.SecurityQuantity.Value) : null, SecurityTransactionType.Dividend => null, _ => e.SecurityQuantity };
                    var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, tradeAmt, e.Subject, e.RecipientName, e.BookingDescription, sub, qty).SetGroup(gid);
                    _db.Postings.Add(main); await UpsertAggregatesAsync(main, ct);
                    if (fee != 0m)
                    {
                        var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid);
                        _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, ct);
                    }
                    if (tax != 0m)
                    {
                        var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid);
                        _db.Postings.Add(taxP); await UpsertAggregatesAsync(taxP, ct);
                    }
                }

                if (contact.IsPaymentIntermediary && ce.SplitDraftId != null)
                {
                    var gidZero = await CreateBankAndContactAsync(ce, 0m);
                    await CreateSecurityAsync(ce, gidZero);
                    await BookSplitDraftGroupAsync(ce.SplitDraftId.Value, ownerUserId, childDraft.Id, self, account, ct, visited);
                }
                else
                {
                    var gid = await CreateBankAndContactAsync(ce, ce.Amount);
                    if (ce.SavingsPlanId != null && ce.ContactId == self.Id)
                    {
                        var sp = new Domain.Postings.Posting(ce.Id, PostingKind.SavingsPlan, null, null, ce.SavingsPlanId, null, ce.BookingDate, -ce.Amount, ce.Subject, ce.RecipientName, ce.BookingDescription, null).SetGroup(gid);
                        _db.Postings.Add(sp); await UpsertAggregatesAsync(sp, ct);
                    }
                    await CreateSecurityAsync(ce, gid);
                }
            }
            childDraft.MarkCommitted();
        }
    }

    private async Task<Guid?> GetNextStatementDraftAsync(StatementDraft draft)
    {
        var nextDraft = await _db.StatementDrafts.OrderBy(d => d.Id)
            .Where(d => d.Status == StatementDraftStatus.Draft && d.Id > draft.Id)
            .FirstOrDefaultAsync();
        if (nextDraft == null)
        {
            nextDraft = await _db.StatementDrafts.OrderBy(d => d.Id)
                .Where(d => d.Status == StatementDraftStatus.Draft)
                .LastOrDefaultAsync();
        }
        return nextDraft?.Id;
    }

    public async Task<(Guid? prevId, Guid? nextId)> GetUploadGroupNeighborsAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var current = await _db.StatementDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (current == null || current.UploadGroupId == null)
        {
            return (null, null);
        }
        // Reversed ordering: now ascending by CreatedUtc then Id (previously descending) to match requested inverse order.
        var orderedIds = await _db.StatementDrafts.AsNoTracking()
            .Where(d => d.OwnerUserId == ownerUserId && d.UploadGroupId == current.UploadGroupId)
            .OrderBy(d => d.CreatedUtc)
            .ThenBy(d => d.Id)
            .Select(d => d.Id)
            .ToListAsync(ct);
        var idx = orderedIds.IndexOf(draftId);
        if (idx == -1)
        {
            return (null, null);
        }
        Guid? prev = idx > 0 ? orderedIds[idx - 1] : null;
        Guid? next = idx < orderedIds.Count - 1 ? orderedIds[idx + 1] : null;
        return (prev, next);
    }

    public async Task<decimal?> GetSplitGroupSumAsync(Guid splitDraftId, Guid ownerUserId, CancellationToken ct)
    {
        var child = await _db.StatementDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.Id == splitDraftId && d.OwnerUserId == ownerUserId, ct);
        if (child == null)
        {
            return null;
        }
        if (child.UploadGroupId == null)
        {
            return await _db.StatementDraftEntries
                .Where(e => e.DraftId == splitDraftId)
                .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        }
        var groupIds = await _db.StatementDrafts.AsNoTracking()
            .Where(d => d.OwnerUserId == ownerUserId && d.UploadGroupId == child.UploadGroupId)
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (groupIds.Count == 0)
        {
            return 0m;
        }
        return await _db.StatementDraftEntries
            .Where(e => groupIds.Contains(e.DraftId))
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
    }
}
