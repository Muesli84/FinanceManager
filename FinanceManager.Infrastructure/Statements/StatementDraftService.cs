using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Reader;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

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

        // Kontoauszug-Draft anlegen
        var draft = new StatementDraft(ownerUserId, originalFileName);

        // Headerdaten (z.B. IBAN) übernehmen
        if (!string.IsNullOrWhiteSpace(parsedDraft.Header.IBAN))
        {
            var account = await _db.Accounts
                .FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId && a.Iban == parsedDraft.Header.IBAN, ct);
            if (account != null)
            {
                draft.SetDetectedAccount(account.Id);
            }
        }

        // Bewegungen übernehmen
        foreach (var movement in parsedDraft.Movements)
        {
            draft.AddEntry(movement.BookingDate, movement.Amount, movement.Subject);
        }

        _db.StatementDrafts.Add(draft);
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
        await _db.SaveChangesAsync(ct);
        return Map(draft);
    }

    public async Task<CommitResult?> CommitAsync(Guid draftId, Guid ownerUserId, Guid accountId, ImportFormat format, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }
        if (!await _db.Accounts.AnyAsync(a => a.Id == accountId && a.OwnerUserId == ownerUserId, ct)) { return null; }

        // create import entity
        var import = new StatementImport(accountId, format, draft.OriginalFileName);
        _db.StatementImports.Add(import);
        await _db.SaveChangesAsync(ct);

        // create statement entries (simplified hash = Guid.NewGuid().ToString())
        foreach (var e in draft.Entries)
        {
            _db.StatementEntries.Add(new StatementEntry(import.Id, e.BookingDate, e.Amount, e.Subject, Guid.NewGuid().ToString()));
        }
        import.GetType().GetProperty("TotalEntries")!.SetValue(import, draft.Entries.Count); // quick set; consider mutator
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

    private static StatementDraftDto Map(StatementDraft draft) => new(
        draft.Id,
        draft.OriginalFileName,
        draft.DetectedAccountId,
        draft.Status,
        draft.Entries.Select(e => new StatementDraftEntryDto(e.Id, e.BookingDate, e.Amount, e.Subject)).ToList());
}
