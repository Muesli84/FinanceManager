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
using FinanceManager.Application.Attachments; // added
using FinanceManager.Domain.Attachments; // added
using System.Globalization;
using FinanceManager.Shared.Extensions; // added

namespace FinanceManager.Infrastructure.Statements;

public sealed partial class StatementDraftService : IStatementDraftService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileReader> _statementFileReaders;
    private readonly IPostingAggregateService _aggregateService;
    private readonly ILogger<StatementDraftService> _logger; // added
    private readonly IAttachmentService? _attachments; // optional to keep compatibility with tests
    private List<StatementDraftDto>? allDrafts = null;
    private List<FinanceManager.Domain.Securities.Security>? allSecurities = null;
    private List<Domain.Accounts.Account>? allAccounts = null;

    // helper: move entry attachments to bank posting and create references for other postings
    private async Task PropagateEntryAttachmentsAsync(Guid ownerUserId, StatementDraftEntry entry, Guid bankPostingId, IEnumerable<Guid> otherPostingIds, CancellationToken ct)
    {
        if (_attachments == null) { return; }
        var list = await _attachments.ListAsync(ownerUserId, AttachmentEntityKind.StatementDraftEntry, entry.Id, 0, 200, ct);
        if (list == null || list.Count == 0) { return; }
        await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraftEntry, entry.Id, AttachmentEntityKind.Posting, bankPostingId, ownerUserId, ct);
        foreach (var att in list)
        {
            foreach (var pid in otherPostingIds)
            {
                await _attachments.CreateReferenceAsync(ownerUserId, AttachmentEntityKind.Posting, pid, att.Id, ct);
            }
        }
    }

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

    public StatementDraftService(AppDbContext db, IPostingAggregateService aggregateService, IEnumerable<IStatementFileReader>? readers = null, ILogger<StatementDraftService>? logger = null, IAttachmentService? attachments = null) // logger param added
    {
        _db = db;
        _aggregateService = aggregateService;
        _logger = logger ?? NullLogger<StatementDraftService>.Instance;
        _attachments = attachments;
        _statementFileReaders = (readers is not null && readers.ToList().Any()) ? readers.ToList() : new List<IStatementFileReader>
        {
            new ING_PDfReader(),
            new ING_StatementFileReader(),
            new Wuestenrot_StatementFileReader(),
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
            //throw new InvalidOperationException("No valid statement file reader found or no movements detected.");
            return;
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
                var draftEntries = (await _db.StatementDraftEntries
                    .Where(e => e.DraftId == draft.DraftId)
                    .Where(e => e.ContactId == account.BankContactId)
                    .Where(e => e.SecurityId == security.Id)
                    .Where(e => e.Amount == line.Amount)
                    .ToListAsync(ct))
                    .Where(e => (e.BookingDate == line.BookingDate) || (line.ValutaDate == e.ValutaDate && e.BookingDate.ToFirstOfMonth() == line.BookingDate.ToFirstOfMonth()))
                    .ToList();
                if (!draftEntries.Any()) continue;
                var entries = draftEntries.ToList();
                if (entries.Count == 0) continue;

                if (entries.Count > 1) {
                    var entry2 = entries.FirstOrDefault();
                    entry2.SetSecurity(entry2.SecurityId, line.PostingDescription switch
                    {
                        "Dividend" => SecurityTransactionType.Dividend,
                        "Sell" => SecurityTransactionType.Sell,
                        "Buy" => SecurityTransactionType.Buy,
                        _ => entry2.SecurityTransactionType
                    }, line.PostingDescription switch
                    {
                        "Dividend" => null,
                        "Sell" => line.Quantity ?? entry2.SecurityQuantity,
                        "Buy" => line.Quantity ?? entry2.SecurityQuantity,
                        _ => entry2.SecurityQuantity
                    }, line.FeeAmount ?? entry2.SecurityFeeAmount, line.TaxAmount ?? entry2.SecurityTaxAmount);
                    await _db.SaveChangesAsync();

                    foreach (var entry3 in entries.Skip(1))
                    {
                        entry3.MarkAlreadyBooked();
                        await _db.SaveChangesAsync();
                    }
                    continue;
                };
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
            await AddStatementDetailsAsync(ownerUserId, originalFileName, fileBytes, ct);
        }

        if (parsedDraft is not null)
        {
            var uploadGroupId = Guid.NewGuid();

            // Erweiterung: MinEntries laden
            var userSettings = await _db.Users.AsNoTracking()
                .Where(u => u.Id == ownerUserId)
                .Select(u => new
                {
                    u.ImportSplitMode,
                    u.ImportMaxEntriesPerDraft,
                    u.ImportMonthlySplitThreshold,
                    u.ImportMinEntriesPerDraft
                })
                .SingleOrDefaultAsync(ct);

            var mode = userSettings?.ImportSplitMode ?? ImportSplitMode.MonthlyOrFixed;
            var maxPerDraft = userSettings?.ImportMaxEntriesPerDraft ?? 250;
            var monthlyThreshold = userSettings?.ImportMonthlySplitThreshold ?? maxPerDraft;
            var minPerDraft = userSettings?.ImportMinEntriesPerDraft ?? 1;

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
                if (size <= 0)
                {
                    yield return source.ToList();
                    yield break;
                }
                for (int i = 0; i < source.Count; i += size)
                {
                    yield return source.Skip(i).Take(size).ToList();
                }
            }

            // Zwischendarstellung für spätere Min-Merge-Logik
            var preliminaryGroups = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();

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
                        preliminaryGroups.Add((monthLabel, parts[0], false, g.Key.Year, g.Key.Month));
                    }
                    else
                    {
                        for (int p = 0; p < parts.Count; p++)
                        {
                            preliminaryGroups.Add(($"{monthLabel} (Teil {p + 1})", parts[p], true, g.Key.Year, g.Key.Month));
                        }
                    }
                }

                // MinEntries-Merge nur anwenden wenn:
                // - monatlicher Modus effektiv
                // - minPerDraft > 1
                // - Gruppe kein gesplitteter Teil (IsSplitPart == false)
                if (minPerDraft > 1)
                {
                    preliminaryGroups = ApplyMonthlyMinMerge(preliminaryGroups, minPerDraft);
                }
            }
            else
            {
                var chunks = Chunk(allMovements, maxPerDraft).ToList();
                if (chunks.Count == 1)
                {
                    preliminaryGroups.Add((string.Empty, chunks[0], false, null, null));
                }
                else
                {
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        preliminaryGroups.Add(($"(Teil {i + 1})", chunks[i], false, null, null));
                    }
                }
            }

            // Finalisieren + Logging
            var largest = preliminaryGroups.Count == 0 ? 0 : preliminaryGroups.Max(g => g.Movements.Count);
            LastImportSplitInfo = new ImportSplitInfo(mode, useMonthly, preliminaryGroups.Count, allMovements.Count, maxPerDraft, largest, monthlyThreshold);

            _logger.LogInformation("ImportSplit result: Mode={Mode} EffectiveUseMonthly={UseMonthly} Movements={Movements} Drafts={DraftCount} MaxEntriesPerDraft={MaxPerDraft} LargestDraftSize={Largest} Threshold={Threshold} File={File} MinEntries={Min}",
                mode, useMonthly, allMovements.Count, preliminaryGroups.Count, maxPerDraft, largest, monthlyThreshold, originalFileName, minPerDraft);

            var baseDescription = parsedDraft.Header.Description;

            foreach (var group in preliminaryGroups)
            {
                ct.ThrowIfCancellationRequested();

                var draft = await CreateDraftHeader(ownerUserId, originalFileName, fileBytes, parsedDraft, ct);
                draft.SetUploadGroup(uploadGroupId);

                if (!string.IsNullOrWhiteSpace(group.Label))
                {
                    draft.Description = string.IsNullOrWhiteSpace(baseDescription)
                        ? group.Label
                        : $"{baseDescription} {group.Label}";
                }

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
    }

    /// <summary>
    /// Wendet die Mindestanzahl-Regel auf monatliche (nicht weiter gesplittete) Gruppen an.
    /// Strategien (aus Tests abgeleitet):
    /// 1. Führende kleine Monate vor erstem großen: in Blöcken >= Min bündeln; Rest mit erstem großen Monat mergen.
    /// 2. Kleine Monate am Ende analog (wenn eigenständiger Block >= Min bildbar, separat lassen; sonst mit letztem großen mergen).
    /// 3. Einzelner kleiner Monat zwischen zwei großen -> an rechten großen hängen (Test {20,3,20} -> {20,23}).
    /// 4. Mehrere kleine Monate zwischen zwei großen -> balanciert verteilen, indem immer die aktuell kleinere Seite den nächsten kleinen Monat übernimmt (Test {19,1,1,19} -> {20,20}).
    /// 5. Reine Sequenz nur kleiner Monate -> greedy Blöcke bilden bis >= Min, letzter evtl. kleiner Block mit vorigem mergen.
    /// 6. Folge kleiner Monate vor großem Monatsblock: Baue möglichst Blöcke exakt = Min (Test 1,1,1,1,1,1,20 Min=5 -> {5,21}).
    /// </summary>
    private static List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>
        ApplyMonthlyMinMerge(List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)> input, int min)
    {
        // Extrahiere nur Monats-Gruppen, Reihenfolge beibehalten
        var list = input;

        // Schneller Exit
        if (!list.Any(g => !g.IsSplitPart && g.Movements.Count < min))
        {
            return list;
        }

        // Arbeite auf Kopieliste
        var result = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();

        // Hilfsfunktionen
        static bool IsAnchor((string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month) g, int minEntries)
            => !g.IsSplitPart && g.Movements.Count >= minEntries;

        static bool IsSmall((string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month) g, int minEntries)
            => !g.IsSplitPart && g.Movements.Count < minEntries;

        // Gesamte Sequenz aller kleinen ohne Anker?
        bool anyAnchor = list.Any(g => IsAnchor(g, min));
        if (!anyAnchor)
        {
            // Alle sind klein -> greedy Gruppen >= min
            var buffer = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();
            var acc = new List<StatementMovement>();
            var monthLabels = new List<string>();

            void FlushGroup(bool force)
            {
                if (acc.Count == 0) return;
                if (force || acc.Count >= min)
                {
                    var label = BuildMergedLabel(monthLabels);
                    result.Add((label, acc.ToList(), false, null, null));
                    acc.Clear();
                    monthLabels.Clear();
                }
            }

            foreach (var g in list)
            {
                acc.AddRange(g.Movements);
                monthLabels.Add(g.Label.Split(' ')[0]);
                if (acc.Count >= min)
                {
                    FlushGroup(force: true);
                }
            }

            // Rest falls zu klein -> an vorherige Gruppe anhängen
            if (acc.Count > 0)
            {
                if (result.Count == 0)
                {
                    // Einzige Gruppe (zu klein) -> lassen (fachlich kaum relevant, aber kein Anchor vorhanden)
                    var labelSingle = BuildMergedLabel(monthLabels);
                    result.Add((labelSingle, acc, false, null, null));
                }
                else
                {
                    var last = result[^1];
                    last.Movements.AddRange(acc);
                    var appendedLabel = $"{last.Label}+{string.Join('+', monthLabels)}";
                    result[^1] = (appendedLabel, last.Movements, false, null, null);
                }
            }

            return result;
        }

        int index = 0;
        while (index < list.Count)
        {
            var current = list[index];

            // Split-Parts oder Anker unverändert hinzufügen (können später kleine davor aufnehmen)
            if (current.IsSplitPart)
            {
                result.Add(current);
                index++;
                continue;
            }

            if (IsAnchor(current, min))
            {
                // Prüfe nachfolgenden Run kleiner Monate zwischen diesem und nächstem Anchor
                var smallRun = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();
                int look = index + 1;
                while (look < list.Count && IsSmall(list[look], min))
                {
                    smallRun.Add(list[look]);
                    look++;
                }

                bool hasRightAnchor = look < list.Count && IsAnchor(list[look], min);

                if (smallRun.Count == 0)
                {
                    // Kein kleiner Run direkt danach
                    result.Add(current);
                    index++;
                    continue;
                }

                // Fall: kleiner Run am Ende ohne rechten Anchor
                if (!hasRightAnchor)
                {
                    // Versuche eigenständige Gruppen aus dem Run zu bilden (wie trailing logic)
                    var trailingGroups = BuildStandaloneOrAttach(smallRun, min);
                    if (trailingGroups.attachToPrevious)
                    {
                        // Alles an current anhängen
                        foreach (var sm in smallRun)
                        {
                            current.Movements.AddRange(sm.Movements);
                            current = (MergeTwoLabels(current.Label, sm.Label), current.Movements, false, null, null);
                        }
                        result.Add(current);
                    }
                    else
                    {
                        // Current zuerst, dann eigenständige Gruppen
                        result.Add(current);
                        foreach (var g in trailingGroups.groups!)
                        {
                            result.Add(g);
                        }
                    }
                    index = look;
                    continue;
                }

                // Zwischen zwei Anchors:
                // Sonderfall: genau 1 kleiner Monat -> an rechten Anchor hangen (Test-Konvention)
                if (smallRun.Count == 1)
                {
                    result.Add(current); // linker Anchor unverändert
                    // Den einen kleinen Monat NICHT hier hinzufügen, sondern später beim rechten Anchor
                    // -> Wir markieren ihn zum Überspringen und mergen beim rechten Anchor
                    // Speicherung in Hilfsstruktur: wir lassen ihn einfach stehen und mergen später beim Eintreffen des rechten Anchors
                    index++; // current
                    // Der kleine Monat wird beim normalen Durchlauf als "small vor Anchor" erkannt
                    continue;
                }

                // Mehrere kleine Monate -> balanciert verteilen
                var leftAnchor = current;
                var rightAnchor = list[look];

                var leftMovs = new List<StatementMovement>(leftAnchor.Movements);
                var rightMovs = new List<StatementMovement>(rightAnchor.Movements);
                string leftLabel = leftAnchor.Label;
                string rightLabel = rightAnchor.Label;

                // Iteriere kleine Monate in Reihenfolge; immer der (aktuellen) kleineren Seite zuordnen, bei Gleichstand links
                foreach (var sm in smallRun)
                {
                    if (leftMovs.Count <= rightMovs.Count)
                    {
                        leftMovs.AddRange(sm.Movements);
                        leftLabel = MergeTwoLabels(leftLabel, sm.Label);
                    }
                    else
                    {
                        rightMovs.AddRange(sm.Movements);
                        rightLabel = MergeTwoLabels(rightLabel, sm.Label);
                    }
                }

                // Linken Anchor aktualisieren, rechten Anchor später behandeln -> wir ersetzen rechten später vollständig
                result.Add((leftLabel, leftMovs, false, leftAnchor.Year, leftAnchor.Month));

                // Rechter Anchor wird übersprungen und neu eingefügt
                list[look] = (rightLabel, rightMovs, false, rightAnchor.Year, rightAnchor.Month);

                index = look; // fahre beim rechten Anchor fort (der jetzt evtl. noch weitere Runs hat)
                continue;
            }
            else
            {
                // current ist kleiner Monat (kein Anchor) – kann nur Leading-Run sein (vor erstem Anchor) oder einzelner kleiner vor Anchor (aus Sonderfall oben)
                var leadingRun = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();
                int l = index;
                while (l < list.Count && IsSmall(list[l], min))
                {
                    leadingRun.Add(list[l]);
                    l++;
                }
                bool hasFollowingAnchor = l < list.Count && IsAnchor(list[l], min);

                if (!hasFollowingAnchor)
                {
                    // (Sollte durch anyAnchor true praktisch nicht auftreten – aber fallback)
                    var built = BuildStandaloneOrAttach(leadingRun, min);
                    if (built.attachToPrevious)
                    {
                        // Keine Previous vorhanden -> standalone (letzte Gruppe evtl. < min akzeptieren)
                        foreach (var g in leadingRun)
                        {
                            result.Add(g);
                        }
                    }
                    else
                    {
                        foreach (var g in built.groups!)
                        {
                            result.Add(g);
                        }
                    }
                    index = l;
                    continue;
                }

                // Leading vor Anchor -> bilde möglichst Blöcke exakt = min, Rest an Anchor
                var blocks = new List<(List<StatementMovement> movs, List<string> labels)>();
                var accMovs = new List<StatementMovement>();
                var accLabels = new List<string>();

                foreach (var g in leadingRun)
                {
                    accMovs.AddRange(g.Movements);
                    accLabels.Add(g.Label.Split(' ')[0]);
                    if (accMovs.Count >= min)
                    {
                        blocks.Add((new List<StatementMovement>(accMovs), new List<string>(accLabels)));
                        accMovs.Clear();
                        accLabels.Clear();
                    }
                }

                // Übriggebliebene kleine (accMovs) werden an Anchor gehängt
                foreach (var b in blocks)
                {
                    var label = BuildMergedLabel(b.labels);
                    result.Add((label, b.movs, false, null, null));
                }

                if (accMovs.Count > 0)
                {
                    // an Anchor anhängen -> wir modifizieren den Anchor jetzt
                    var anchor = list[l];
                    anchor.Movements.InsertRange(0, accMovs); // vorne einfügen (zeitlich frühere Monate)
                    anchor = (MergeTwoLabels(BuildMergedLabel(accLabels), anchor.Label), anchor.Movements, false, anchor.Year, anchor.Month);
                    list[l] = anchor;
                }

                index = l; // Anchor selbst wird in nachfolgender Iteration verarbeitet
            }
        }

        return result;

        // Hilfen -----------------------------------------------------

        static (bool attachToPrevious, List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>? groups)
            BuildStandaloneOrAttach(List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)> run, int minEntries)
        {
            var groups = new List<(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month)>();
            var accMovs = new List<StatementMovement>();
            var accLabels = new List<string>();

            foreach (var g in run)
            {
                accMovs.AddRange(g.Movements);
                accLabels.Add(g.Label.Split(' ')[0]);
                if (accMovs.Count >= minEntries)
                {
                    groups.Add((BuildMergedLabel(accLabels), new List<StatementMovement>(accMovs), false, null, null));
                    accMovs.Clear();
                    accLabels.Clear();
                }
            }

            if (accMovs.Count > 0)
            {
                // Rest zu klein -> signal attach to previous
                if (groups.Count == 0)
                {
                    return (attachToPrevious: true, null);
                }
                // Rest an letzte Gruppe anhängen
                var last = groups[^1];
                last.Movements.AddRange(accMovs);
                last = (MergeTwoLabels(last.Label, BuildMergedLabel(accLabels)), last.Movements, false, null, null);
                groups[^1] = last;
            }
            return (false, groups);
        }

        static string BuildMergedLabel(IEnumerable<string> monthLabels)
        {
            var arr = monthLabels.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if (arr.Count == 0) return string.Empty;
            if (arr.Count == 1) return arr[0];
            return string.Join('+', arr);
        }

        static string MergeTwoLabels(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a)) return b;
            if (string.IsNullOrWhiteSpace(b)) return a;
            // Doppelte vermeiden
            var parts = (a + "+" + b).Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var distinct = parts.Distinct().ToList();
            return string.Join('+', distinct);
        }
    }

    private async Task<Guid> EnsureStatementsSystemCategoryAsync(Guid ownerUserId, CancellationToken ct)
    {
        // Sprache des Users ermitteln (PreferredLanguage, z.B. "de" / "en")
        var lang = await _db.Users.AsNoTracking()
            .Where(u => u.Id == ownerUserId)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(ct);

        var name = GetStatementsCategoryName(lang);
        // Suchen: system category mit exakt diesem Namen
        var existing = await _db.AttachmentCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.IsSystem && c.Name == name, ct);
        if (existing != null)
        {
            return existing.Id;
        }

        // Anlegen (IsSystem = true)
        var cat = new AttachmentCategory(ownerUserId, name, isSystem: true);
        _db.AttachmentCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return cat.Id;
    }

    private static string GetStatementsCategoryName(string? preferredLanguage)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(preferredLanguage))
            {
                var culture = new CultureInfo(preferredLanguage);
                var two = culture.TwoLetterISOLanguageName.ToLowerInvariant();
                return two switch
                {
                    "en" => "Bank statements",
                    // weitere Sprachen später ergänzbar
                    _ => "Kontoauszüge"
                };
            }
        }
        catch (CultureNotFoundException)
        {
            // Fallback unten
        }
        return "Kontoauszüge";
    }

    private async Task<StatementDraft> CreateDraftHeader(Guid ownerUserId, string originalFileName, byte[] fileBytes, StatementParseResult parsedDraft, CancellationToken ct)
    {
        var draft = new StatementDraft(ownerUserId, originalFileName, parsedDraft.Header.AccountNumber, parsedDraft.Header.Description);

        // store original via attachment service when available
        if (_attachments != null)
        {
            // NEW: Kategorie "Kontoauszüge" (lokalisiert) ermitteln/erzeugen
            var catId = await EnsureStatementsSystemCategoryAsync(ownerUserId, ct);

            using var ms = new MemoryStream(fileBytes, writable: false);
            await _attachments.UploadAsync(
                ownerUserId,
                AttachmentEntityKind.StatementDraft,
                draft.Id,
                ms,
                originalFileName,
                "application/octet-stream",
                catId,
                ct);
        }

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

        // Move attachments from draft to account on commit
        if (_attachments != null)
        {
            await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraft, draft.Id, AttachmentEntityKind.Account, accountId, ownerUserId, ct);
        }

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
            // Require different upload groups
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
        // Ignore entries that are already booked in validation scope
        var scopedEntryQuery = _db.StatementDraftEntries.Where(e => e.DraftId == draftId && (entryId == null || entryId == e.Id))
            .Where(e => e.Status != StatementDraftEntryStatus.AlreadyBooked && e.Status != StatementDraftEntryStatus.Announced);

        var entries = await scopedEntryQuery.ToArrayAsync(ct);

        void Add(string code, string sev, string msg, Guid? draftId, Guid? eId) => messages.Add(new DraftValidationMessageDto(code, sev, msg, draftId ?? draft.Id, eId));

        if (draft.DetectedAccountId == null)
        {
            Add("NO_ACCOUNT", "Error", "Kein Konto zugeordnet.", null, null);
        }

        var self = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);
        var account = draft.DetectedAccountId == null
            ? null
            : await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId, ct);

        // Cycle detection for split relations
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
            Add("SPLIT_CYCLE_DETECTED", "Error", "[Split] Zyklen in Split-Verknüpfungen erkannt.", null, null);
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
            var groupEntries = groupDrafts.SelectMany(d => d.Entries)
                .Where(e => e.Status != StatementDraftEntryStatus.AlreadyBooked)
                .ToList();
            if (groupDrafts.Any(d => d.DetectedAccountId != null))
            {
                Add("SPLIT_DRAFT_HAS_ACCOUNT", "Error", prefix + "Split-Draft darf kein Konto zugeordnet haben.", draft.Id, parentEntry.Id);
            }
            var sum = groupEntries.Sum(e => e.Amount);
            if (sum != parentEntry.Amount)
            {
                Add("SPLIT_AMOUNT_MISMATCH", "Error", prefix + "Summe der Aufteilung entspricht nicht dem Ursprungsbetrag.", draft.Id, parentEntry.Id);
            }
            foreach (var ce in groupEntries)
            {
                if (ce.ContactId == null)
                {
                    Add("ENTRY_NO_CONTACT", "Error", prefix + "Kein Kontakt zugeordnet.", ce.DraftId, ce.Id);
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, token);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (ce.SplitDraftId == null)
                    {
                        Add("INTERMEDIARY_NO_SPLIT", "Error", prefix + "Zahlungsdienst ohne weitere Aufteilung.", ce.DraftId, ce.Id);
                    }
                    else
                    {
                        await ValidateSplitBranchAsync(ce, prefix, token);
                    }
                }
                if (self != null && ce.ContactId == self.Id && ce.SavingsPlanId == null)
                {
                    Add("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", prefix + "Für Eigentransfer ist ein Sparplan sinnvoll.", ce.DraftId, ce.Id);
                }
                if (ce.SecurityId != null && account != null)
                {
                    if (ce.ContactId != account.BankContactId)
                    {
                        Add("SECURITY_INVALID_CONTACT", "Error", prefix + "Wertpapierbuchung erfordert Bankkontakt des Kontos.", ce.DraftId, ce.Id);
                    }
                    if (ce.SecurityTransactionType == null)
                    {
                        Add("SECURITY_MISSING_TXTYPE", "Error", prefix + "Wertpapier: Transaktionstyp fehlt.", ce.DraftId, ce.Id);
                    }
                    if (ce.SecurityTransactionType != null && ce.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (ce.SecurityQuantity == null || ce.SecurityQuantity <= 0m)
                        {
                            Add("SECURITY_MISSING_QUANTITY", "Error", prefix + "Wertpapier: Stückzahl fehlt.", ce.DraftId, ce.Id);
                        }
                    }
                    if (ce.SecurityTransactionType == SecurityTransactionType.Dividend && ce.SecurityQuantity != null)
                    {
                        Add("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", prefix + "Wertpapier: Menge ist bei Dividende nicht zulässig.", ce.DraftId, ce.Id);
                    }
                    var fee = ce.SecurityFeeAmount ?? 0m;
                    var tax = ce.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(ce.Amount))
                    {
                        Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", prefix + "Wertpapier: Gebühren+Steuern übersteigen Betrag.", ce.DraftId, ce.Id);
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
                    Add("ENTRY_NO_CONTACT", "Error", "Kein Kontakt zugeordnet.", null, e.Id);
                    continue;
                }
                if (e.Status == StatementDraftEntryStatus.Open)
                {
                    Add("ENTRY_NEEDS_CHECK", "Error", "Eine Prüfung der Angaben ist erforderlich.", null, e.Id);
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.ContactId && x.OwnerUserId == ownerUserId, ct);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (e.SplitDraftId == null)
                    {
                        Add("INTERMEDIARY_NO_SPLIT", "Error", "Zahlungsdienst ohne Aufteilungs-Entwurf.", null, e.Id);
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
                        Add("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", "Für Eigentransfer ist ein Sparplan sinnvoll.", null, e.Id);
                    }
                    else if (account != null && account.Type == AccountType.Savings)
                    {
                        Add("SAVINGSPLAN_INVALID_ACCOUNT", "Error", "Sparplan auf Sparkonto ist nicht zulässig.",   null, e.Id);
                    }
                }
                if (e.SecurityId != null && account != null)
                {
                    if (e.ContactId != account.BankContactId)
                    {
                        Add("SECURITY_INVALID_CONTACT", "Error", "Wertpapierbuchung erfordert Bankkontakt des Kontos.", null, e.Id);
                    }
                    if (e.SecurityTransactionType == null)
                    {
                        Add("SECURITY_MISSING_TXTYPE", "Error", "Wertpapier: Transaktionstyp fehlt.", null, e.Id);
                    }
                    if (e.SecurityTransactionType != null && e.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (e.SecurityQuantity == null || e.SecurityQuantity <= 0m)
                        {
                            Add("SECURITY_MISSING_QUANTITY", "Error", "Wertpapier: Stückzahl fehlt.", null, e.Id);
                        }
                    }
                    if (e.SecurityTransactionType == SecurityTransactionType.Dividend && e.SecurityQuantity != null)
                    {
                        Add("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", "Wertpapier: Menge ist bei Dividende nicht zulässig.", null, e.Id);
                    }
                    var fee = e.SecurityFeeAmount ?? 0m;
                    var tax = e.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(e.Amount))
                    {
                        Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Wertpapier: Gebühren+Steuern übersteigen Betrag.", null, e.Id);
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

        // Savings plan extra info
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
            DateTime latestBookingDate = entries.Max(e => e.BookingDate).Date;
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

        // ignore entries already booked
        var toBook = (entryId == null ? allEntriesScope : allEntriesScope.Where(e => e.Id == entryId.Value))
            .Where(e => e.Status != StatementDraftEntryStatus.AlreadyBooked && e.Status != StatementDraftEntryStatus.Announced)
            .ToList();

        var updatedPlanIds = new HashSet<Guid>();
        async Task UpdateRecurringPlanIfDueAsync(Guid planId, DateTime bookingDate, CancellationToken token)
        {
            if (updatedPlanIds.Contains(planId)) { return; }
            var plan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Id == planId, token);
            if (plan == null) { return; }
            if (plan.Type != SavingsPlanType.Recurring || plan.TargetDate == null || plan.Interval == null) { return; }

            // Use domain logic with month-end rule, no business-day logic
            if (plan.AdvanceTargetDateIfDue(bookingDate))
            {
                _db.SavingsPlans.Update(plan);
                await _db.SaveChangesAsync(token);
            }

            updatedPlanIds.Add(plan.Id);
        }

        async Task<(Guid groupId, Guid bankPostingId, Guid contactPostingId)> CreateBankAndContactPostingAsync(StatementDraftEntry e, decimal amount, Guid? contactId, CancellationToken token)
        {
            var gid = Guid.NewGuid();
            var bankPosting = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
            _db.Postings.Add(bankPosting); await UpsertAggregatesAsync(bankPosting, token);
            var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
            _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, token);

            // Kontosaldo anpassen (nur Bank-Buchung relevant)
            if (amount != 0m)
            {
                account.AdjustBalance(amount);
            }

            // After saving, bankPosting.Id and contactPosting.Id will be set; return both ids
            return (gid, bankPosting.Id, contactPosting.Id);
        }

        // update calls accordingly

        async Task CreateSecurityPostingsAsync(StatementDraftEntry e, Guid gid, CancellationToken token)
        {
            if (e.SecurityId == null) { return; }
            var fee = Math.Abs(e.SecurityFeeAmount ?? 0m); var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
            if (e.Amount < 0) { fee = -fee; tax = -tax; }
            var factor = e.SecurityTransactionType switch { SecurityTransactionType.Buy => 1, SecurityTransactionType.Sell => -1, SecurityTransactionType.Dividend => -1, _ => -1 };
            var tradeAmount = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.Amount - fee - tax, SecurityTransactionType.Sell => e.Amount + fee + tax, SecurityTransactionType.Dividend => e.Amount + fee + tax, _ => e.Amount + fee + tax };
            var tradeSub = e.SecurityTransactionType switch { SecurityTransactionType.Buy => SecurityPostingSubType.Buy, SecurityTransactionType.Sell => SecurityPostingSubType.Sell, SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend, _ => SecurityPostingSubType.Buy };
            decimal? qty = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.SecurityQuantity.HasValue ? Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Sell => e.SecurityQuantity.HasValue ? -Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Dividend => null, _ => e.SecurityQuantity };
            var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, tradeAmount, e.Subject, e.RecipientName, e.BookingDescription, tradeSub, qty).SetGroup(gid);
            _db.Postings.Add(main); await UpsertAggregatesAsync(main, token);
            if (fee != 0m)
            {
                var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid);
                _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, token);
            }
            if (tax != 0m)
            {
                var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid);
                _db.Postings.Add(taxP); await UpsertAggregatesAsync(taxP, token);
            }
        }

        foreach (var e in toBook)
        {
            if (e.ContactId == null) { continue; }
            var c = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == e.ContactId && x.OwnerUserId == ownerUserId, ct);
            if (c == null) { continue; }
            if (c.IsPaymentIntermediary && e.SplitDraftId != null)
            {
                // create postings with amount 0 for parent
                var (parentGid, parentBankId, parentContactId) = await CreateBankAndContactPostingAsync(e, 0m, e.ContactId, ct);
                await CreateSecurityPostingsAsync(e, parentGid, ct);

                // propagate attachments from entry to postings
                if (_attachments != null)
                {
                    var allForGroup = _db.Postings.Local.Where(p => p.SourceId == e.Id && p.GroupId == parentGid).ToList();
                    var bank = allForGroup.FirstOrDefault(p => p.Kind == PostingKind.Bank);
                    var others = allForGroup.Where(p => p.Id != bank!.Id).Select(p => p.Id).ToList();
                    if (bank != null)
                    {
                        await PropagateEntryAttachmentsAsync(ownerUserId, e, bank.Id, others, ct);
                    }
                }

                // pass valuta date of parent entry as override so child postings get parent's valuta
                var overrideValuta = e.ValutaDate ?? e.BookingDate;
                await BookSplitDraftGroupAsync(e.SplitDraftId.Value, ownerUserId, draft.Id, self, account, ct, new HashSet<Guid>(), overrideValuta, parentBankId, parentContactId);
            }
            else
            {
                // normal booking
                var (gid, bankId, contactId) = await CreateBankAndContactPostingAsync(e, e.Amount, e.ContactId, ct);
                // optional savings plan posting
                Guid? spPostingId = null;
                if (e.SavingsPlanId != null && e.ContactId == self.Id)
                {
                    var spPosting = new Domain.Postings.Posting(e.Id, PostingKind.SavingsPlan, null, null, e.SavingsPlanId, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, -e.Amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(spPosting); await UpsertAggregatesAsync(spPosting, ct);
                    spPostingId = spPosting.Id;

                    // Advance recurring savings plan target date if due (month-end rule), using booking date
                    await UpdateRecurringPlanIfDueAsync(e.SavingsPlanId.Value, e.BookingDate, ct);
                }
                await CreateSecurityPostingsAsync(e, gid, ct);

                // resolve created postings for this entry to propagate attachments
                if (_attachments != null)
                {
                    var allForGroup = _db.Postings.Local.Where(p => p.SourceId == e.Id && p.GroupId == gid).ToList();
                    var bank = allForGroup.FirstOrDefault(p => p.Kind == PostingKind.Bank);
                    var others = allForGroup.Where(p => p.Id != bank!.Id).Select(p => p.Id).ToList();
                    if (spPostingId.HasValue && !others.Contains(spPostingId.Value)) { others.Add(spPostingId.Value); }
                    if (bank != null)
                    {
                        await PropagateEntryAttachmentsAsync(ownerUserId, e, bank.Id, others, ct);
                    }
                }
            }
        }

        // PARTIAL BOOKING: remove only booked entry and keep draft open when other entries remain
        if (entryId != null)
        {
            // remove the processed entry/entries from the draft
            foreach (var e in toBook)
            {
                _db.StatementDraftEntries.Remove(e);
            }
            await _db.SaveChangesAsync(ct);

            bool anyRemaining = await _db.StatementDraftEntries.AnyAsync(x => x.DraftId == draft.Id, ct);
            if (!anyRemaining)
            {
                draft.MarkCommitted();
                await _db.SaveChangesAsync(ct);

                if (_attachments != null)
                {
                    await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraft, draft.Id, AttachmentEntityKind.Account, account.Id, ownerUserId, ct);
                }
            }

            return new BookingResult(true, false, validation, null, toBook.Count, await GetNextStatementDraftAsync(draft));
        }

        // FULL BOOKING: commit whole draft
        draft.MarkCommitted();
        await _db.SaveChangesAsync(ct);
        if (_attachments != null)
        {
            await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraft, draft.Id, AttachmentEntityKind.Account, account.Id, ownerUserId, ct);
        }

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

    private async Task BookSplitDraftGroupAsync(Guid representativeSplitDraftId, Guid ownerUserId, Guid parentDraftId, Contact self, Account account, CancellationToken ct, HashSet<Guid> visited, DateTime? overrideValuta = null, Guid? parentBankPostingId = null, Guid? parentContactPostingId = null)
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

                async Task<(Guid groupId, Guid bankId, Guid contactId)> CreateBankAndContactAsync(StatementDraftEntry e, decimal amount)
                {
                    var gid = Guid.NewGuid();
                    var bank = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(bank); await UpsertAggregatesAsync(bank, ct);
                    var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid);
                    _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, ct);

                    // Kontosaldo anpassen (nur Bank-Buchung relevant)
                    if (amount != 0m)
                    {
                        account.AdjustBalance(amount);
                    }

                    // set parent references if provided
                    if (parentBankPostingId.HasValue)
                    {
                        bank.SetParent(parentBankPostingId.Value);
                    }
                    if (parentContactPostingId.HasValue)
                    {
                        contactPosting.SetParent(parentContactPostingId.Value);
                    }

                    return (gid, bank.Id, contactPosting.Id);
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
                    var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, tradeAmt, e.Subject, e.RecipientName, e.BookingDescription, sub, qty).SetGroup(gid);
                    _db.Postings.Add(main); await UpsertAggregatesAsync(main, ct);
                    if (parentBankPostingId.HasValue) main.SetParent(parentBankPostingId.Value);
                    if (fee != 0m)
                    {
                        var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid);
                        _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, ct);
                        if (parentBankPostingId.HasValue) feeP.SetParent(parentBankPostingId.Value);
                    }
                    if (tax != 0m)
                    {
                        var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid);
                        _db.Postings.Add(taxP); await UpsertAggregatesAsync(taxP, ct);
                        if (parentBankPostingId.HasValue) taxP.SetParent(parentBankPostingId.Value);
                    }
                }

                if (overrideValuta.HasValue)
                    ce.OverrideValutaDate(overrideValuta.Value);

                if (contact.IsPaymentIntermediary && ce.SplitDraftId != null)
                {
                    var (gidZero, bankId, contactId) = await CreateBankAndContactAsync(ce, 0m);
                    await CreateSecurityAsync(ce, gidZero);

                    // propagate attachments from entry to postings
                    if (_attachments != null)
                    {
                        var allForGroup = _db.Postings.Local.Where(p => p.SourceId == ce.Id && p.GroupId == gidZero).ToList();
                        var bank = allForGroup.FirstOrDefault(p => p.Kind == PostingKind.Bank);
                        var others = allForGroup.Where(p => p.Id != bank!.Id).Select(p => p.Id).ToList();
                        if (bank != null)
                        {
                            await PropagateEntryAttachmentsAsync(ownerUserId, ce, bank.Id, others, ct);
                        }
                    }

                    await BookSplitDraftGroupAsync(ce.SplitDraftId.Value, ownerUserId, childDraft.Id, self, account, ct, visited, overrideValuta, bankId, contactId);
                }
                else
                {
                    var (gid, bankId, contactId) = await CreateBankAndContactAsync(ce, ce.Amount);
                    Guid? spId = null;
                    if (ce.SavingsPlanId != null && ce.ContactId == self.Id)
                    {
                        var sp = new Domain.Postings.Posting(ce.Id, PostingKind.SavingsPlan, null, null, ce.SavingsPlanId, null, ce.BookingDate, ce.ValutaDate ?? ce.BookingDate, -ce.Amount, ce.Subject, ce.RecipientName, ce.BookingDescription, null).SetGroup(gid);
                        _db.Postings.Add(sp); await UpsertAggregatesAsync(sp, ct);
                        if (parentBankPostingId.HasValue) sp.SetParent(parentBankPostingId.Value);
                        if (parentContactPostingId.HasValue) sp.SetParent(parentContactPostingId.Value);
                        spId = sp.Id;
                    }
                    await CreateSecurityAsync(ce, gid);

                    if (_attachments != null)
                    {
                        var allForGroup = _db.Postings.Local.Where(p => p.SourceId == ce.Id && p.GroupId == gid).ToList();
                        var bank = allForGroup.FirstOrDefault(p => p.Kind == PostingKind.Bank);
                        var others = allForGroup.Where(p => p.Id != bank!.Id).Select(p => p.Id).ToList();
                        if (spId.HasValue && !others.Contains(spId.Value)) { others.Add(spId.Value); }
                        if (bank != null)
                        {
                            await PropagateEntryAttachmentsAsync(ownerUserId, ce, bank.Id, others, ct);
                        }
                    }
                }
            }
            childDraft.MarkCommitted();
            await _db.SaveChangesAsync(ct);
            // NEW: move child draft attachments to the bank account as well
            if (_attachments != null)
            {
                await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraft, childDraft.Id, AttachmentEntityKind.Account, account.Id, ownerUserId, ct);
            }
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
        var orderedIds = await _db.StatementDrafts.AsNoTracking()
            .Where(d => d.OwnerUserId == ownerUserId && d.UploadGroupId == current.UploadGroupId && d.Status != StatementDraftStatus.Committed)
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

    public async Task<StatementDraftEntryDto?> ResetDuplicateEntryAsync(Guid draftId, Guid entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft)
        {
            return null;
        }
        var entry = draft.Entries.FirstOrDefault(e => e.Id == entryId);
        if (entry == null)
        {
            return null;
        }
        if (entry.Status != StatementDraftEntryStatus.AlreadyBooked)
        {
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

        entry.ResetOpen();
        entry.ClearContact();
        entry.SetSecurity(null, null, null, null, null);
        await _db.SaveChangesAsync(ct);

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

}
