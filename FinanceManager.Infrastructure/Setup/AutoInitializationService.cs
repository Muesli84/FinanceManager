using FinanceManager.Application.Accounts;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace FinanceManager.Infrastructure.Setup
{

    public sealed class AutoInitializationService : IAutoInitializationService
    {
        private readonly ILogger<AutoInitializationService> _logger;
        private readonly IHostEnvironment _env;
        private readonly AppDbContext _db;
        private readonly ISetupImportService _setupImportService;
        private readonly IStatementDraftService _statementDraftService;
        private readonly IContactService _contactService;

        public AutoInitializationService(
            ILogger<AutoInitializationService> logger,
            IHostEnvironment env,
            AppDbContext db,
            ISetupImportService setupImportService,
            IStatementDraftService statementDraftService,
            IContactService contactService)
        {
            _logger = logger;
            _env = env;
            _db = db;
            _setupImportService = setupImportService;
            _statementDraftService = statementDraftService;
            _contactService = contactService;
        }

        public void Run()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(300));
                RunAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto initialization failed.");
            }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            try
            {
                // Find first user that has the Admin role
                var adminRole = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == "Admin", ct);
                if (adminRole == null)
                {
                    _logger.LogInformation("AutoInit: No 'Admin' role found – initialization skipped.");
                    return;
                }

                var admin = await (from u in _db.Users.AsNoTracking()
                                   join ur in _db.Set<IdentityUserRole<Guid>>().AsNoTracking() on u.Id equals ur.UserId
                                   where ur.RoleId == adminRole.Id
                                   orderby u.UserName
                                   select u)
                                  .FirstOrDefaultAsync(ct);

                if (admin == null)
                {
                    _logger.LogInformation("AutoInit: No administrator user found – initialization skipped.");
                    return;
                }

                // init directory determination (first ContentRoot, then BaseDirectory)
                var initDir = Path.Combine(_env.ContentRootPath, "init");
                if (!Directory.Exists(initDir))
                {
                    initDir = Path.Combine(AppContext.BaseDirectory, "init");
                }

                if (!Directory.Exists(initDir))
                {
                    _logger.LogInformation("AutoInit: Kein 'init'-Verzeichnis gefunden – nichts zu tun.");
                    return;
                }

                if (Directory.GetFiles(initDir, "skip").Any())
                {
                    _logger.LogInformation("AutoInit: 'skip'-Datei in 'init'-Verzeichnis gefunden – Vorgang wird übersprungen.");
                    return;
                }
                await ExecuteActions(admin, initDir, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto initialization failed.");
                throw;
            }
        }

        private async Task ExecuteActions(Domain.Users.User admin, string initDir, CancellationToken ct)
        {
            var drafts = new List<StatementDraftDto>();
            drafts.AddRange((await _statementDraftService.GetOpenDraftsAsync(admin.Id, ct)).Select(d => _statementDraftService.GetDraftAsync(d.DraftId, admin.Id, ct).Result));
            var actionFiles = Directory.EnumerateFiles(initDir, "action-*.txt", SearchOption.TopDirectoryOnly);
            foreach (var file in actionFiles)
            {
                try
                {
                    _logger.LogInformation("AutoInit: Verarbeite Aktions-Datei '{File}'.", Path.GetFileName(file));
                    var actions = await File.ReadAllLinesAsync(file, ct);
                    foreach (var action in actions.Select(action => action.Split(":\"").Select(v => v.Trim('"')).ToArray()))
                    {
                        switch (action[0])
                        {
                            case "backup-import":
                                {
                                    // 1) Setup-imports (init-*.json) in order, first file with replace=true
                                    var setupFiles = Directory
                                        .EnumerateFiles(initDir, "init-*.json", SearchOption.TopDirectoryOnly)
                                        .OrderBy(f => Path.GetFileName(f))
                                        .Where(f => Path.GetFileName(f) == action[1])
                                        .ToList();

                                    var isFirst = action.Length > 2 && bool.Parse(action[2]);
                                    foreach (var setupFile in setupFiles)
                                    {
                                        try
                                        {
                                            _logger.LogInformation("AutoInit: Importiere Setup-Datei '{File}' (replace={Replace}).", Path.GetFileName(setupFile), isFirst);
                                            await using var fs = File.OpenRead(setupFile);
                                            await _setupImportService.ImportAsync(admin.Id, fs, isFirst, ct);
                                            isFirst = false;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "AutoInit: Fehler beim Import der Setup-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(setupFile));
                                        }
                                    }
                                    drafts.Clear();
                                    drafts.AddRange((await _statementDraftService.GetOpenDraftsAsync(admin.Id, ct)).Select(d => _statementDraftService.GetDraftAsync(d.DraftId, admin.Id, ct).Result));
                                }
                                break;
                            case "statement-import":
                                {
                                    var draftJson = Directory.EnumerateFiles(initDir, "draft-*.json", SearchOption.TopDirectoryOnly);
                                    var draftCsv = Directory.EnumerateFiles(initDir, "draft-*.csv", SearchOption.TopDirectoryOnly);
                                    var draftPdf = Directory.EnumerateFiles(initDir, "draft-*.pdf", SearchOption.TopDirectoryOnly);
                                    var draftFiles = draftJson.Concat(draftCsv).Concat(draftPdf).OrderBy(f => Path.GetFileName(f)).ToList();

                                    foreach (var draftFile in draftFiles.Where(f => Path.GetFileName(f) == action[1]))
                                    {
                                        try
                                        {
                                            _logger.LogInformation("AutoInit: Importiere Draft-Datei '{File}'.", Path.GetFileName(draftFile));
                                            var bytes = await File.ReadAllBytesAsync(draftFile, ct);
                                            await foreach (var draft in _statementDraftService.CreateDraftAsync(admin.Id, Path.GetFileName(draftFile), bytes, ct))
                                                drafts.Add(draft);
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "AutoInit: Fehler beim Import der Draft-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(draftFile));
                                        }
                                    }
                                }
                                break;
                            case "statement-entry-assignent":
                                {
                                    var offset = drafts.Select(f => Path.GetFileName(f.OriginalFileName)).ToList().IndexOf(action[1]);
                                    var draft = drafts[offset];
                                    var contact = (await _contactService.ListAsync(admin.Id, 0, int.MaxValue, null, action[2], ct)).FirstOrDefault();
                                    if (action.Length > 3)
                                    {
                                        offset = drafts.Select(f => Path.GetFileName(f.OriginalFileName)).ToList().IndexOf(action[3]);
                                        var destDraft = drafts[offset];
                                        await AssignDraftAsync(drafts, draft, destDraft, contact, admin.Id, ct);
                                    }
                                    else
                                        await AssignDraftAsync(drafts, draft, null, contact, admin.Id, ct);
                                    break;
                                }
                            case "statement-entry-remove":
                                await RemoveDraftEntryAsync(drafts, action[1], admin.Id, ct);
                                break;
                            case "statement-posting":
                                {
                                    var offset = 0;
                                    do
                                    {
                                        offset = drafts.Select(f => Path.GetFileName(f.OriginalFileName)).ToList().IndexOf(action[1]);
                                        if (offset >= 0)
                                        {
                                            var draft = drafts[offset];
                                            _logger.LogInformation("Buche Postenpaket {File}.", draft.Description);
                                            if (!draft.Entries.Any())
                                                await _statementDraftService.CancelAsync(draft.DraftId, admin.Id, ct);
                                            else
                                                await PostDraftAsync(draft, admin.Id, ct);
                                            drafts.RemoveAt(offset);
                                        }
                                    } while (offset >= 0);
                                    break;
                                }
                            case "statement-set-savings":
                                {
                                    var savingsPlan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Name == action[2], ct);
                                    if (savingsPlan is not null)
                                    {
                                        var offset = 0;
                                        foreach (var draft in drafts)
                                        {
                                            var draft2 = await _statementDraftService.GetDraftAsync(draft.DraftId, admin.Id, ct);
                                            foreach (var entry in draft2.Entries)
                                                if (entry.Subject == action[1])
                                                {
                                                    var dbEntry = await _db.StatementDraftEntries.FirstAsync(e => e.Id == entry.Id, ct);
                                                    dbEntry.AssignSavingsPlan(savingsPlan.Id);
                                                    if (dbEntry.ContactId.HasValue)
                                                        dbEntry.MarkAccounted(dbEntry.ContactId.Value);
                                                    await _db.SaveChangesAsync(ct);
                                                }
                                        }
                                    }
                                }
                                break;
                            case "statement-set-contact":
                                {
                                    var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Name == action[2], ct);
                                    if (contact is not null)
                                    {
                                        var offset = 0;
                                        foreach (var draft in drafts)
                                        {
                                            var draft2 = await _statementDraftService.GetDraftAsync(draft.DraftId, admin.Id, ct);
                                            foreach (var entry in draft2.Entries)
                                                if (entry.Subject.Contains(action[1]))
                                                {
                                                    var dbEntry = await _db.StatementDraftEntries.FirstAsync(e => e.Id == entry.Id, ct);
                                                    dbEntry.MarkAccounted(contact.Id);
                                                    await _db.SaveChangesAsync(ct);
                                                }
                                        }
                                    }
                                }
                                break;
                            case "savings-set-contract":
                                {
                                    var savingsPlan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Name == action[1], ct);
                                    if (savingsPlan is not null)
                                    {
                                        savingsPlan.SetContractNumber(action[2].Trim());                                        
                                        await _db.SaveChangesAsync(ct);
                                    }
                                }
                                break;
                            case "savings-advancetargetdue":
                                {
                                    foreach (var savingsPlan in await _db.SavingsPlans.ToListAsync(ct))
                                    {
                                        savingsPlan.AdvanceTargetDateIfDue(DateTime.UtcNow);
                                    }
                                    await _db.SaveChangesAsync(ct);
                                }
                                break;
                            case "security-set-alphavantagecode":
                                {
                                    var security = await _db.Securities.FirstOrDefaultAsync(s => s.Name == action[1], ct);
                                    if (security is not null)
                                    {
                                        security.Update(security.Name, security.Identifier, security.Description, action[2].Trim(), security.CurrencyCode, security.CategoryId);
                                        await _db.SaveChangesAsync(ct);
                                    }
                                }
                                break;
                            case "statement-set-security":
                                {
                                    var security = await _db.Securities.FirstOrDefaultAsync(s => s.Name == action[2], ct);
                                    if (!Enum.TryParse<SecurityTransactionType>(action[3], out var transType))
                                        transType = (SecurityTransactionType)(-1);
                                    var quantity = decimal.Parse(action[4]);
                                    var fee = decimal.Parse(action[5]);
                                    var tax = decimal.Parse(action[6]);
                                    foreach (var draft in drafts)
                                    {
                                        var draft2 = await _statementDraftService.GetDraftAsync(draft.DraftId, admin.Id, ct);
                                        foreach (var entry in draft2.Entries)
                                            if (entry.Subject.Contains(action[1]))
                                            {
                                                var dbEntry = await _db.StatementDraftEntries.FirstAsync(e => e.Id == entry.Id, ct);
                                                var entryType = transType;
                                                if (entryType == (SecurityTransactionType)(-1))
                                                {
                                                    if (dbEntry.Amount > 0)
                                                        entryType = SecurityTransactionType.Sell;
                                                    else
                                                        entryType = SecurityTransactionType.Buy;
                                                }
                                                dbEntry.SetSecurity(security.Id, transType, quantity, fee, tax);
                                                if (dbEntry.ContactId.HasValue)
                                                    dbEntry.MarkAccounted(dbEntry.ContactId.Value);
                                                await _db.SaveChangesAsync(ct);
                                            }
                                    }
                                }
                                break;
                            case "statement-set-security-type":
                                {
                                    var transType = (SecurityTransactionType)Enum.Parse(typeof(SecurityTransactionType), action[2]);
                                    foreach (var draft in drafts)
                                    {
                                        var draft2 = await _statementDraftService.GetDraftAsync(draft.DraftId, admin.Id, ct);
                                        foreach (var entry in draft2.Entries)
                                            if (entry.BookingDescription == action[1] || entry.Subject.StartsWith(action[1]))
                                            {
                                                var dbEntry = await _db.StatementDraftEntries.FirstAsync(e => e.Id == entry.Id, ct);
                                                dbEntry.SetSecurity(dbEntry.SecurityId, transType, dbEntry.SecurityQuantity, dbEntry.SecurityFeeAmount, dbEntry.SecurityTaxAmount);
                                                if (dbEntry.ContactId.HasValue)
                                                    dbEntry.MarkAccounted(dbEntry.ContactId.Value);
                                                await _db.SaveChangesAsync(ct);
                                            }
                                    }
                                }
                                break;
                            case "statement-reclassify":
                                {
                                    foreach (var draft in drafts)
                                    {                                        
                                        var draft2 = await _statementDraftService.ClassifyAsync(draft.DraftId, null, admin.Id, ct);
                                        if (draft2 is not null)
                                            MarkDuplicates(draft2);
                                    }
                                }
                                break;
                            case "statement-import-details":
                                {
                                    var statementDetailsFiles = Directory.EnumerateFiles(initDir, "statement-detail*.pdf", SearchOption.TopDirectoryOnly);
                                    foreach (var statementDetailsFile in statementDetailsFiles)
                                    {
                                        try
                                        {
                                            _logger.LogInformation("AutoInit: Importiere Statement-Detail-Datei '{File}'.", Path.GetFileName(statementDetailsFile));
                                            var bytes = await File.ReadAllBytesAsync(statementDetailsFile, ct);
                                            await _statementDraftService.AddStatementDetailsAsync(admin.Id, Path.GetFileName(file), bytes, ct);
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogError(ex, "AutoInit: Fehler beim Import der Statement-Detail-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(file));
                                        }
                                    }
                                }
                                break;
                            case "savings-set-amount":
                                {
                                    var savingsPlan = await _db.SavingsPlans.FirstOrDefaultAsync(p => p.Name == action[1], ct);
                                    if (savingsPlan is not null && decimal.TryParse(action[2], out var newAmount))
                                        savingsPlan.SetTarget(newAmount, savingsPlan.TargetDate);
                                }
                                break;
                            case "statement-checkduplicates":
                                {
                                    string value = "";
                                    foreach (var draft in drafts)
                                    {
                                        MarkDuplicates(draft);

                                        foreach (var entry in draft.Entries
                                            .Where(e => e.SecurityId is not null && e.SecurityId != Guid.Empty)
                                            .Where(e => !e.BookingDescription.Contains("Zins"))
                                            .Where(e => !e.Subject.Contains("Zins"))
                                            .Where(e => e.Subject.StartsWith("WP-ABRECHNUNG ")))
                                        {
                                            var name = entry.Subject.Replace("WP-ABRECHNUNG ", "").Trim().Split(' ').First();
                                            var security = await _db.Securities.FirstOrDefaultAsync(s => s.Id == entry.SecurityId, ct);
                                            var line = $"\"statement-set-security\":\"{name}\":\"{security.Name}\":\"\":\"\":\"\":\"\"";
                                            value += line + Environment.NewLine;
                                        }
                                    }
                                    Console.WriteLine(value);

                                }
                                break;

                        }
                        await _db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AutoInit: Fehler beim Import der Draft-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(file));
                }
            }
        }

        private void MarkDuplicates(StatementDraftDto draft)
        {
            // Ermittelt innerhalb des Drafts Duplikate auf Basis der geforderten Felder
            // (BookingDate, ValutaDate, Amount, ContactId, SecurityId, SavingsPlanId)
            // und setzt deren Status auf Open.
            var entries = _db.StatementDraftEntries
                .Where(e => e.DraftId == draft.DraftId)
                .ToList();

            var duplicateGroups = entries
                .GroupBy(e => new
                {
                    Booking = e.BookingDate.Date,
                    Valuta = e.ValutaDate?.Date,
                    e.Amount,
                    e.ContactId,
                    e.SecurityId,
                    e.SavingsPlanId
                })
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicateGroups)
            {
                foreach (var entry in group.Skip(1))
                {
                    _db.StatementDraftEntries.Remove(entry);
                }
            }

            if (duplicateGroups.Count > 0)
            {
                _db.SaveChanges();
            }
        }

        private async Task PostDraftAsync(StatementDraftDto draft, Guid ownerId, CancellationToken ct)
        {
            try
            {
                var result = await _statementDraftService.BookAsync(draft.DraftId, null, ownerId, true, ct);
                if (result.Success) return;
                var entriesWithMessages = result.Validation.Messages.Select(m => new { DraftId = m.DraftId, EntryId = m.EntryId });
                foreach (var entry in draft.Entries)
                {
                    if (entriesWithMessages.Any(em => em.EntryId == entry.Id)) continue;
                    await _statementDraftService.BookAsync(draft.DraftId, entry.Id, ownerId, true, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoInit: Fehler beim Buchen eines Kontoauszugs. ({file})", draft.OriginalFileName);
            }
        }

        private async Task RemoveDraftEntryAsync(List<StatementDraftDto> drafts, string text, Guid ownerId, CancellationToken ct)
        {
            foreach (var currDraft in drafts)
            {
                foreach (var entry in currDraft.Entries.Where(e => !string.IsNullOrWhiteSpace(e.RecipientName)).Where(e => e.RecipientName.Contains(text)))
                    await _statementDraftService.UpdateEntryCoreAsync(currDraft.DraftId, entry.Id, ownerId, entry.BookingDate, entry.ValutaDate, 0, entry.Subject, entry.RecipientName, entry.CurrencyCode, entry.BookingDescription, ct);
            }
        }

        private async Task AssignDraftAsync(List<StatementDraftDto> drafts, StatementDraftDto draft, StatementDraftDto destDraft, ContactDto? contact, Guid ownerId, CancellationToken ct)
        {
            foreach (var currDraft in drafts.Where(d => d.DraftId != draft.DraftId).Where(d => destDraft is null || d.DraftId == destDraft.DraftId))
            {
                foreach (var entry in currDraft.Entries.Where(e => e.ContactId == contact.Id))
                {
                    await _statementDraftService.SetEntrySplitDraftAsync(currDraft.DraftId, entry.Id, draft.DraftId, ownerId, ct);
                }
            }
        }
    }
}
