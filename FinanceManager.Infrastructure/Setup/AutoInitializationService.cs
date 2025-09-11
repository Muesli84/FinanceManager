using FinanceManager.Application.Accounts;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Contacts;
using FinanceManager.Infrastructure.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
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
                // Ersten Admin-Benutzer ermitteln
                var admin = await _db.Users
                    .AsNoTracking()
                    .Where(u => u.IsAdmin)
                    .OrderBy(u => u.Username) // deterministisch
                    .FirstOrDefaultAsync(ct);

                if (admin == null)
                {
                    _logger.LogInformation("AutoInit: Kein Administrator gefunden – Initialisierung übersprungen.");
                    return;
                }

                // init-Verzeichnis bestimmen (erst ContentRoot, dann BaseDirectory)
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

                _logger.LogInformation("AutoInit: Starte Initialisierung aus '{InitDir}' für Admin '{Admin}'.", initDir, admin.Username);

                // 1) Setup-Importe (init-*.json) in Reihenfolge, erste Datei mit replace=true
                var setupFiles = Directory
                    .EnumerateFiles(initDir, "init-*.json", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => Path.GetFileName(f))
                    .ToList();

                var isFirst = true;
                foreach (var file in setupFiles)
                {
                    try
                    {
                        _logger.LogInformation("AutoInit: Importiere Setup-Datei '{File}' (replace={Replace}).", Path.GetFileName(file), isFirst);
                        await using var fs = File.OpenRead(file);
                        await _setupImportService.ImportAsync(admin.Id, fs, isFirst, ct);
                        isFirst = false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AutoInit: Fehler beim Import der Setup-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(file));
                    }
                }

                // 2) Statement-Drafts (draft-*.csv / draft-*.pdf)
                var draftCsv = Directory.EnumerateFiles(initDir, "draft-*.csv", SearchOption.TopDirectoryOnly);
                var draftPdf = Directory.EnumerateFiles(initDir, "draft-*.pdf", SearchOption.TopDirectoryOnly);
                var draftFiles = draftCsv.Concat(draftPdf).OrderBy(f => Path.GetFileName(f)).ToList();
                var drafts = new List<StatementDraftDto>();
                foreach (var file in draftFiles)
                {
                    try
                    {
                        _logger.LogInformation("AutoInit: Importiere Draft-Datei '{File}'.", Path.GetFileName(file));
                        var bytes = await File.ReadAllBytesAsync(file, ct);
                        drafts.Add(await _statementDraftService.CreateDraftAsync(admin.Id, Path.GetFileName(file), bytes, ct));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AutoInit: Fehler beim Import der Draft-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(file));
                    }
                }

                var actionFiless = Directory.EnumerateFiles(initDir, "action-*.txt", SearchOption.TopDirectoryOnly);
                foreach (var file in actionFiless)
                {
                    try
                    {
                        _logger.LogInformation("AutoInit: Verarbeite Aktions-Datei '{File}'.", Path.GetFileName(file));
                        var actions = await File.ReadAllLinesAsync(file, ct);
                        foreach (var action in actions.Select(action => action.Split(':')))
                        {
                            switch(action[0])
                            {
                                case "statement-entry-assignent":
                                    {
                                        var offset = draftFiles.Select(f => Path.GetFileName(f)).ToList().IndexOf(action[1]);
                                        var draft = drafts[offset];
                                        var contact = (await _contactService.ListAsync(admin.Id, 0, int.MaxValue, null, action[2], ct)).FirstOrDefault();
                                        await AssignDraftAsync(drafts, draft, contact, admin.Id, ct);
                                        break;
                                    }
                                case "statement-entry-remove":
                                    await RemoveDraftEntryAsync(drafts, action[1], admin.Id, ct);
                                    break;
                                case "statement-posting":
                                    {
                                        var offset = draftFiles.Select(f => Path.GetFileName(f)).ToList().IndexOf(action[1]);
                                        var draft = drafts[offset];
                                        await PostDraftAsync(draft, admin.Id, ct);
                                        break;
                                    }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AutoInit: Fehler beim Import der Draft-Datei '{File}'. Setze mit nächster Datei fort.", Path.GetFileName(file));
                    }
                }

                _logger.LogInformation("AutoInit: Initialisierung abgeschlossen. SetupFiles={SetupCount}, DraftFiles={DraftCount}.", setupFiles.Count, draftFiles.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto initialization failed.");
                throw;
            }
        }

        private async Task PostDraftAsync(StatementDraftDto draft, Guid ownerId, CancellationToken ct)
        {
            await _statementDraftService.BookAsync(draft.DraftId, ownerId, true, ct);
        }

        private async Task RemoveDraftEntryAsync(List<StatementDraftDto> drafts, string text, Guid ownerId, CancellationToken ct)
        {
            foreach (var currDraft in drafts)
            {
                foreach (var entry in currDraft.Entries.Where(e => !string.IsNullOrWhiteSpace(e.RecipientName)).Where(e => e.RecipientName.Contains(text)))
                    await _statementDraftService.UpdateEntryCoreAsync(currDraft.DraftId, entry.Id, ownerId, entry.BookingDate, entry.ValutaDate, 0, entry.Subject, entry.RecipientName, entry.CurrencyCode, entry.BookingDescription, ct);
            }
        }

        private async Task AssignDraftAsync(List<StatementDraftDto> drafts, StatementDraftDto draft, ContactDto? contact, Guid ownerId, CancellationToken ct)
        {
            foreach (var currDraft in drafts.Where(d => d.DraftId != draft.DraftId))
            {
                foreach (var entry in currDraft.Entries.Where(e => e.ContactId == contact.Id))
                {
                    await _statementDraftService.SetEntrySplitDraftAsync(currDraft.DraftId, entry.Id, draft.DraftId, ownerId, ct);
                }
            }
        }
    }
}
