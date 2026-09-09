using FinanceManager.Application.Aggregates;
using FinanceManager.Application.Attachments; // new
using FinanceManager.Application.Setup;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Attachments;
using FinanceManager.Domain.Budget;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Postings; // AggregatePeriod, PostingAggregate
using FinanceManager.Domain.Reports;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements; // for StatementDraft
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Infrastructure;
using iText.StyledXmlParser.Jsoup.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static FinanceManager.Domain.Attachments.Attachment;

/// <summary>
/// Service that imports application setup and backup data into the database.
/// Supports multiple backup versions and maps legacy identifiers to newly created entities.
/// </summary>
public sealed class SetupImportService : ISetupImportService
{
    private readonly AppDbContext _db;
    private readonly IStatementDraftService _statementDraftService;
    private readonly IPostingAggregateService _aggregateService;
    private readonly IAttachmentService _attachments;
    private readonly ILogger<SetupImportService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupImportService"/> class.
    /// </summary>
    /// <param name="db">Application database context used to persist imported entities.</param>
    /// <param name="statementDraftService">Statement draft service used when creating drafts from legacy ledger exports.</param>
    /// <param name="aggregateService">Aggregate posting service used to rebuild posting aggregates after import.</param>
    /// <param name="attachments">Optional attachment service used to persist embedded files; when null a no-op implementation is used.</param>
    /// <param name="logger">Optional logger for progress and diagnostics.</param>
    public SetupImportService(AppDbContext db, IStatementDraftService statementDraftService, IPostingAggregateService aggregateService, ILogger<SetupImportService> logger, IAttachmentService? attachments = null)
    {
        _db = db;
        _statementDraftService = statementDraftService;
        _aggregateService = aggregateService;
        _attachments = attachments ?? new NoopAttachmentService();
        _logger = logger;
    }

    private sealed class NoopAttachmentService : IAttachmentService
    {
        public Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AttachmentDto>>(Array.Empty<AttachmentDto>());

        public Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AttachmentDto>>(Array.Empty<AttachmentDto>());

        public Task<int> CountAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct)
            => Task.FromResult(0);

        public Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct)
            => Task.FromResult(new AttachmentDto(Guid.Empty, (short)kind, entityId, fileName, contentType ?? "application/octet-stream", 0L, categoryId, DateTime.UtcNow, false));

        // New overload with role to satisfy IAttachmentService
        public Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, AttachmentRole role, CancellationToken ct)
            => UploadAsync(ownerUserId, kind, entityId, content, fileName, contentType, categoryId, ct);

        public Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct)
        {
            var name = string.IsNullOrWhiteSpace(fileName) ? url : fileName!;
            return Task.FromResult(new AttachmentDto(Guid.Empty, (short)kind, entityId, name, "text/uri-list", 0L, categoryId, DateTime.UtcNow, true));
        }

        public Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
            => Task.FromResult<(Stream, string, string)?>(null);

        public Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct)
            => Task.FromResult(false);

        public Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct)
            => Task.FromResult(false);

        public Task<bool> UpdateCoreAsync(Guid ownerUserId, Guid attachmentId, string? fileName, Guid? categoryId, CancellationToken ct)
            => Task.FromResult(false);

        public Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<AttachmentDto> CreateReferenceAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid masterAttachmentId, CancellationToken ct)
            => Task.FromResult(new AttachmentDto(Guid.Empty, (short)kind, entityId, "ref", "application/octet-stream", 0L, null, DateTime.UtcNow, false));

    }

    /// <summary>
    /// Progress information reported during import operations.
    /// </summary>
    public struct ImportProgress
    {
        /// <summary>
        /// Current main step index (1-based semantic).
        /// </summary>
        public int Step { get; set; }

        /// <summary>
        /// Total number of main steps.
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Current sub-step index within the current main step.
        /// </summary>
        public int SubStep { get; set; }

        /// <summary>
        /// Total number of sub-steps for the current main step.
        /// </summary>
        public int SubTotal { get; set; }

        /// <summary>
        /// Human readable description of the current step.
        /// </summary>
        public string StepDescription { get; internal set; }

        internal ImportProgress Inc()
        {
            Step++;
            InitSub(0);
            return this;
        }
        internal ImportProgress IncSub(int subTotal = 0)
        {
            if (subTotal > 0) SubTotal = subTotal;
            SubStep++;
            return this;
        }

        internal ImportProgress InitSub(int count)
        {
            SubTotal = count;
            SubStep = 0;
            return this;
        }

        internal ImportProgress SetDescription(string description)
        {
            StepDescription = description;
            return this;
        }
    }

    /// <summary>
    /// Event raised when import progress changes. Subscribers receive an <see cref="ImportProgress"/> instance.
    /// </summary>
    public event EventHandler<ImportProgress>? ProgressChanged;

    /// <summary>
    /// Imports a backup stream for the specified user.
    /// The backup format is newline-delimited NDJSON: first line contains metadata, remaining data contains JSON payload.
    /// Supports multiple backup versions; higher versions are preferred when available.
    /// </summary>
    /// <param name="userId">The owner user id for which data should be imported.</param>
    /// <param name="fileStream">Stream containing the backup data (will be read but not disposed by this method).</param>
    /// <param name="replaceExisting">When true, existing user data is cleared before importing.</param>
    /// <param name="ct">Cancellation token to cancel the import operation.</param>
    /// <returns>A task that completes when the import has finished.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the provided stream does not contain valid backup metadata or unsupported version.</exception>
    public async Task ImportAsync(Guid userId, Stream fileStream, bool replaceExisting, CancellationToken ct)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8);

        // Erste Zeile: Metadaten-Objekt
        var metaLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(metaLine))
            throw new InvalidOperationException("Backup-Metadaten fehlen.");

        var meta = JsonSerializer.Deserialize<BackupMeta>(metaLine);
        if (meta == null || meta.Type != "Backup" || meta.Version < 2)
            throw new InvalidOperationException("Ung�ltiges Backup-Format.");
        var jsonData = await reader.ReadToEndAsync();
        switch (meta.Version)
        {
            case 3:
                await ImportVersion3(jsonData, userId, replaceExisting, ct);
                break;
        }
    }

    private async Task ImportVersion3(string jsonData, Guid userId, bool replaceExisting, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonData);
        var root = doc.RootElement;

        var progress = new ImportProgress()
        {
            StepDescription = "",
            Step = 0,
            Total = 34 - (replaceExisting ? 0 : 1),
            SubStep = 0,
            SubTotal = 0
        };
        ProgressChanged?.Invoke(this, progress);

        if (replaceExisting)
        {
            ProgressChanged?.Invoke(this, progress.SetDescription("Clearing user data"));
            await _db.ClearUserDataAsync(userId, (step, count) =>
            {
                progress.SubStep = step;
                progress.SubTotal = count;
                ProgressChanged?.Invoke(this, progress);
            }, ct);
            await _db.SaveChangesAsync(ct);
            ProgressChanged?.Invoke(this, progress.Inc());
        }

        // Maps from backup Id -> new Id
        var contactCatMap = new Dictionary<Guid, Guid>();
        var contactMap = new Dictionary<Guid, Guid>();
        var aliasMap = new Dictionary<Guid, Guid>();
        var securityCatMap = new Dictionary<Guid, Guid>();
        var securityMap = new Dictionary<Guid, Guid>();
        var securityPricesMap = new Dictionary<Guid, Guid>();
        var savingsCatMap = new Dictionary<Guid, Guid>();
        var savingsMap = new Dictionary<Guid, Guid>();
        var accountMap = new Dictionary<Guid, Guid>();
        var draftMap = new Dictionary<Guid, Guid>();
        var draftEntryMap = new Dictionary<Guid, Guid>();
        var postingMap = new Dictionary<Guid, Guid>();
        var favoriteMap = new Dictionary<Guid, Guid>();
        var reportMap = new Dictionary<Guid, Guid>();
        var attachmentCatMap = new Dictionary<Guid, Guid>();
        var budgetCategoryMap = new Dictionary<Guid, Guid>();
        var budgetPurposeMap = new Dictionary<Guid, Guid>();

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // ContactCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Contact Categories"));
        if (root.TryGetProperty("ContactCategories", out var contactCategories) && contactCategories.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.ContactCategory.ContactCategoryBackupDto>>(contactCategories.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.ContactCategory.ContactCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: ContactCategory create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                if (string.IsNullOrWhiteSpace(dto.Name)) { progress.IncSub(); continue; }
                var entity = new ContactCategory(userId, dto.Name);
                _db.ContactCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                contactCatMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Contacts
        ProgressChanged?.Invoke(this, progress.SetDescription("Contacts"));
        if (root.TryGetProperty("Contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.Contact.ContactBackupDto>>(contacts.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.Contact.ContactBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: Contact create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                var categoryId = dto.CategoryId.HasValue && contactCatMap.TryGetValue(dto.CategoryId.Value, out var m) ? m : (Guid?)null;
                var entity = new Contact(userId, dto.Name, dto.Type, categoryId, dto.Description, dto.IsPaymentIntermediary);
                if (entity.Type == ContactType.Self)
                {
                    var existing = await _db.Contacts.FirstOrDefaultAsync(ec => ec.Type == ContactType.Self && ec.OwnerUserId == userId);
                    if (existing is null)
                        _db.Contacts.Add(entity);
                    else
                    {
                        existing.SetCategory(entity.CategoryId);
                        existing.Rename(entity.Name);
                        existing.SetPaymentIntermediary(entity.IsPaymentIntermediary);
                        existing.SetDescription(entity.Description);
                        entity = existing;
                    }
                }
                else
                    _db.Contacts.Add(entity);
                await _db.SaveChangesAsync(ct);
                contactMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // AliasNames
        ProgressChanged?.Invoke(this, progress.SetDescription("Contact Alias Names"));
        if (root.TryGetProperty("AliasNames", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.AliasName.AliasNameBackupDto>>(aliases.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.AliasName.AliasNameBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: AliasName create - BackupId={BackupId}, Pattern={Pattern}, ContactOldId={ContactId}", dto.Id, dto.Pattern, dto.ContactId);
                if (!contactMap.TryGetValue(dto.ContactId, out var mappedContact)) { ProgressChanged?.Invoke(this, progress.IncSub()); continue; }
                var entity = new AliasName(mappedContact, dto.Pattern);
                _db.AliasNames.Add(entity);
                await _db.SaveChangesAsync(ct);
                aliasMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SecurityCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Security Categories"));
        if (root.TryGetProperty("SecurityCategories", out var secCats) && secCats.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.SecurityCategory.SecurityCategoryBackupDto>>(secCats.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.SecurityCategory.SecurityCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: SecurityCategory create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                var entity = new SecurityCategory(userId, dto.Name);
                _db.SecurityCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                securityCatMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Securities
        ProgressChanged?.Invoke(this, progress.SetDescription("Securities"));
        if (root.TryGetProperty("Securities", out var secs) && secs.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.Security.SecurityBackupDto>>(secs.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.Security.SecurityBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: Security create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                Guid? categoryId = null;
                if (dto.CategoryId.HasValue && securityCatMap.TryGetValue(dto.CategoryId.Value, out var mapped)) categoryId = mapped;
                var entity = new Security(userId, dto.Name, dto.Identifier, dto.Description, dto.AlphaVantageCode, dto.CurrencyCode, categoryId);
                _db.Securities.Add(entity);
                await _db.SaveChangesAsync(ct);
                securityMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SecurityPrices
        ProgressChanged?.Invoke(this, progress.SetDescription("Security Prices"));
        if (root.TryGetProperty("SecurityPrices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.SecurityPrice.SecurityPriceBackupDto>>(prices.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.SecurityPrice.SecurityPriceBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: SecurityPrice create - BackupId={BackupId}, SecurityId={SecurityId}, Date={Date}", dto.Id, dto.SecurityId, dto.Date);
                if (!securityMap.TryGetValue(dto.SecurityId, out var mappedSid)) { ProgressChanged?.Invoke(this, progress.IncSub()); continue; }
                var entity = new SecurityPrice(mappedSid, dto.Date, dto.Close);
                _db.SecurityPrices.Add(entity);
                await _db.SaveChangesAsync(ct);
                securityPricesMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SavingsPlanCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Savings Plan Categories"));
        if (root.TryGetProperty("SavingsPlanCategories", out var spCats) && spCats.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Savings.SavingsPlanCategory.SavingsPlanCategoryBackupDto>>(spCats.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Savings.SavingsPlanCategory.SavingsPlanCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: SavingsPlanCategory create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                var entity = new SavingsPlanCategory(userId, dto.Name);
                _db.SavingsPlanCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                savingsCatMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SavingsPlans
        ProgressChanged?.Invoke(this, progress.SetDescription("Savings Plans"));
        if (root.TryGetProperty("SavingsPlans", out var sps) && sps.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Savings.SavingsPlan.SavingsPlanBackupDto>>(sps.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Savings.SavingsPlan.SavingsPlanBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: SavingsPlan create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                Guid? categoryId = null;
                if (dto.CategoryId.HasValue && savingsCatMap.TryGetValue(dto.CategoryId.Value, out var mapped)) categoryId = mapped;
                var entity = new SavingsPlan(userId, dto.Name, dto.Type, dto.TargetAmount, dto.TargetDate, dto.Interval, categoryId);
                if (!string.IsNullOrWhiteSpace(dto.ContractNumber)) entity.SetContractNumber(dto.ContractNumber);
                _db.SavingsPlans.Add(entity);
                await _db.SaveChangesAsync(ct);
                savingsMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Accounts
        ProgressChanged?.Invoke(this, progress.SetDescription("Bank Accounts"));
        if (root.TryGetProperty("Accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Accounts.Account.AccountBackupDto>>(accounts.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Accounts.Account.AccountBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: Account create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                var bankContactOld = dto.BankContactId;
                Guid mappedBankContact;
                if (!bankContactOld.Equals(Guid.Empty) && contactMap.TryGetValue(bankContactOld, out var mbc)) mappedBankContact = mbc;
                else
                {
                    var bank = new Contact(userId, dto.Name, ContactType.Bank, null);
                    _db.Contacts.Add(bank);
                    await _db.SaveChangesAsync(ct);
                    mappedBankContact = bank.Id;
                }
                var entity = new Account(userId, dto.Type, dto.Name, dto.Iban, mappedBankContact);
                _db.Accounts.Add(entity);
                await _db.SaveChangesAsync(ct);
                accountMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // BudgetCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Budget Categories"));
        if (root.TryGetProperty("BudgetCategories", out var budgetCategoriesEl) && budgetCategoriesEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Budget.BudgetCategory.BudgetCategoryBackupDto>>(budgetCategoriesEl.GetRawText(), jsonOptions)
                ?? new List<FinanceManager.Domain.Budget.BudgetCategory.BudgetCategoryBackupDto>();

            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                {
                    ProgressChanged?.Invoke(this, progress.IncSub());
                    continue;
                }

                var entity = new BudgetCategory(userId, dto.Name);
                _db.BudgetCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                budgetCategoryMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // BudgetPurposes
        ProgressChanged?.Invoke(this, progress.SetDescription("Budget Purposes"));
        if (root.TryGetProperty("BudgetPurposes", out var budgetPurposesEl) && budgetPurposesEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Budget.BudgetPurpose.BudgetPurposeBackupDto>>(budgetPurposesEl.GetRawText(), jsonOptions)
                ?? new List<FinanceManager.Domain.Budget.BudgetPurpose.BudgetPurposeBackupDto>();

            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var mappedSourceId = dto.SourceId;
                mappedSourceId = dto.SourceType switch
                {
                    BudgetSourceType.Contact when contactMap.TryGetValue(dto.SourceId, out var mc) => mc,
                    BudgetSourceType.ContactGroup when contactCatMap.TryGetValue(dto.SourceId, out var mg) => mg,
                    BudgetSourceType.SavingsPlan when savingsMap.TryGetValue(dto.SourceId, out var ms) => ms,
                    _ => mappedSourceId
                };

                Guid? mappedCategoryId = null;
                if (dto.BudgetCategoryId.HasValue && budgetCategoryMap.TryGetValue(dto.BudgetCategoryId.Value, out var mappedCat))
                {
                    mappedCategoryId = mappedCat;
                }

                var entity = new BudgetPurpose(userId, dto.Name, dto.SourceType, mappedSourceId, dto.Description, dto.ValuationType);
                entity.SetCategory(mappedCategoryId);

                _db.BudgetPurposes.Add(entity);
                await _db.SaveChangesAsync(ct);
                budgetPurposeMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // BudgetRules
        ProgressChanged?.Invoke(this, progress.SetDescription("Budget Rules"));
        if (root.TryGetProperty("BudgetRules", out var budgetRulesEl) && budgetRulesEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Budget.BudgetRule.BudgetRuleBackupDto>>(budgetRulesEl.GetRawText(), jsonOptions)
                ?? new List<FinanceManager.Domain.Budget.BudgetRule.BudgetRuleBackupDto>();

            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                Guid? mappedPurposeId = null;
                Guid? mappedCategoryId = null;

                if (dto.BudgetPurposeId.HasValue && dto.BudgetPurposeId.Value != Guid.Empty)
                {
                    if (!budgetPurposeMap.TryGetValue(dto.BudgetPurposeId.Value, out var mp))
                    {
                        ProgressChanged?.Invoke(this, progress.IncSub());
                        continue;
                    }
                    mappedPurposeId = mp;
                }
                else if (dto.BudgetCategoryId.HasValue && dto.BudgetCategoryId.Value != Guid.Empty)
                {
                    if (!budgetCategoryMap.TryGetValue(dto.BudgetCategoryId.Value, out var mc))
                    {
                        ProgressChanged?.Invoke(this, progress.IncSub());
                        continue;
                    }
                    mappedCategoryId = mc;
                }
                else
                {
                    ProgressChanged?.Invoke(this, progress.IncSub());
                    continue;
                }

                var entity = new BudgetRule(userId, mappedPurposeId, mappedCategoryId, dto.Amount, dto.Interval, dto.StartDate, dto.EndDate, dto.CustomIntervalMonths);
                _db.BudgetRules.Add(entity);
                await _db.SaveChangesAsync(ct);
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // BudgetOverrides
        ProgressChanged?.Invoke(this, progress.SetDescription("Budget Overrides"));
        if (root.TryGetProperty("BudgetOverrides", out var budgetOverridesEl) && budgetOverridesEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Budget.BudgetOverride.BudgetOverrideBackupDto>>(budgetOverridesEl.GetRawText(), jsonOptions)
                ?? new List<FinanceManager.Domain.Budget.BudgetOverride.BudgetOverrideBackupDto>();

            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                if (!budgetPurposeMap.TryGetValue(dto.BudgetPurposeId, out var mappedPurposeId))
                {
                    ProgressChanged?.Invoke(this, progress.IncSub());
                    continue;
                }

                var period = new BudgetPeriodKey(dto.PeriodYear, dto.PeriodMonth);
                var entity = new BudgetOverride(userId, mappedPurposeId, period, dto.Amount);
                _db.BudgetOverrides.Add(entity);
                await _db.SaveChangesAsync(ct);
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Postings
        var postingCount = 0;
        ProgressChanged?.Invoke(this, progress.SetDescription("Postings"));
        Dictionary<Guid, Posting> postingMapCollection = new Dictionary<Guid, Posting>();
        if (root.TryGetProperty("Postings", out var postArr) && postArr.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Postings.Posting.PostingBackupDto>>(postArr.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Postings.Posting.PostingBackupDto>();
            postingCount = list.Count;
            ProgressChanged?.Invoke(this, progress.InitSub(postingCount));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: Posting create - BackupId={BackupId}, BookingDate={BookingDate}, Amount={Amount}", dto.Id, dto.BookingDate, dto.Amount);
                Guid? accountId = dto.AccountId.HasValue && accountMap.TryGetValue(dto.AccountId.Value, out var a) ? a : null;
                Guid? contactId = dto.ContactId.HasValue && contactMap.TryGetValue(dto.ContactId.Value, out var c) ? c : null;
                Guid? savingsPlanId = dto.SavingsPlanId.HasValue && savingsMap.TryGetValue(dto.SavingsPlanId.Value, out var s) ? s : null;
                Guid? securityId = dto.SecurityId.HasValue && securityMap.TryGetValue(dto.SecurityId.Value, out var sd) ? sd : null;
                var entity = new FinanceManager.Domain.Postings.Posting(
                    dto.SourceId,
                    dto.Kind,
                    accountId,
                    contactId,
                    savingsPlanId,
                    securityId,
                    dto.BookingDate,
                    dto.Amount,
                    dto.Subject,
                    dto.RecipientName,
                    dto.Description,
                    dto.SecuritySubType,
                    dto.Quantity);
                if (dto.GroupId.HasValue && dto.GroupId != Guid.Empty) entity.SetGroup(dto.GroupId.Value);
                _db.Postings.Add(entity);
                postingMapCollection.Add(dto.Id, entity);
                if (progress.SubStep % 100 == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    foreach (var kvp in postingMapCollection)
                        postingMap[kvp.Key] = kvp.Value.Id;
                    postingMapCollection.Clear();
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
            foreach (var kvp in postingMapCollection)
                postingMap[kvp.Key] = kvp.Value.Id;
            postingMapCollection.Clear();
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // StatementDrafts + Entries
        ProgressChanged?.Invoke(this, progress.SetDescription("Statement Drafts"));
        if (root.TryGetProperty("StatementDrafts", out var drafts) && drafts.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Statements.StatementDraft.StatementDraftBackupDto>>(drafts.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Statements.StatementDraft.StatementDraftBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: StatementDraft create - BackupId={BackupId}, OriginalFile={OriginalFile}", dto.Id, dto.OriginalFileName);
                var originalFileName = string.IsNullOrWhiteSpace(dto.OriginalFileName) ? "backup" : dto.OriginalFileName;
                var entity = new StatementDraft(userId, originalFileName, dto.AccountName, dto.Description, dto.Status);
                if (dto.DetectedAccountId.HasValue && accountMap.TryGetValue(dto.DetectedAccountId.Value, out var mapped)) entity.SetDetectedAccount(mapped);
                // Note: legacy embedded file bytes handled in older format; skip unless explicitly present in JSON root as "OriginalFileContent" entries
                _db.StatementDrafts.Add(entity);
                await _db.SaveChangesAsync(ct);
                draftMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Statement Draft Entries"));
        Dictionary<Guid, StatementDraftEntry> statementDraftEntryCollection = new Dictionary<Guid, StatementDraftEntry>();
        if (root.TryGetProperty("StatementDraftEntries", out var draftEntries) && draftEntries.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Statements.StatementDraftEntry.StatementDraftEntryBackupDto>>(draftEntries.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Statements.StatementDraftEntry.StatementDraftEntryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            var entryNo = 0;
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: StatementDraftEntry create - BackupId={BackupId}, BookingDate={BookingDate}, Amount={Amount}", dto.Id, dto.BookingDate, dto.Amount);
                if (!draftMap.TryGetValue(dto.DraftId, out var draftId)) { ProgressChanged?.Invoke(this, progress.IncSub()); continue; }
                var draft = await _db.StatementDrafts.FirstAsync(x => x.Id == draftId, ct);

                var entry = new StatementDraftEntry(
                    ++entryNo,
                    draft.Id,
                    dto.BookingDate,
                    dto.Amount,
                    dto.Subject,
                    dto.RecipientName,
                    dto.ValutaDate,
                    dto.CurrencyCode,
                    dto.BookingDescription,
                    dto.IsAnnounced,
                    dto.IsCostNeutral,
                    dto.Status);
                if (dto.ContactId.HasValue && contactMap.TryGetValue(dto.ContactId.Value, out var mappedC)) entry.AssignContactWithoutAccounting(mappedC);
                if (dto.SavingsPlanId.HasValue && savingsMap.TryGetValue(dto.SavingsPlanId.Value, out var mappedS)) entry.AssignSavingsPlan(mappedS);
                if (dto.ArchiveSavingsPlanOnBooking) entry.SetArchiveSavingsPlanOnBooking(true);
                if (dto.SplitDraftId.HasValue && draftMap.TryGetValue(dto.SplitDraftId.Value, out var mappedSplit)) entry.AssignSplitDraft(mappedSplit);
                if (dto.SecurityId.HasValue)
                {
                    Guid? mapped = null; if (securityMap.TryGetValue(dto.SecurityId.Value, out var m)) mapped = m;
                    entry.SetSecurity(mapped, dto.SecurityTransactionType, dto.SecurityQuantity, dto.SecurityFeeAmount, dto.SecurityTaxAmount);
                }
                _db.StatementDraftEntries.Add(entry);
                statementDraftEntryCollection.Add(dto.Id, entry);
                if (progress.SubStep % 100 == 0)
                {
                    await _db.SaveChangesAsync(ct);
                    foreach (var kvp in statementDraftEntryCollection)
                        draftEntryMap[kvp.Key] = kvp.Value.Id;
                    statementDraftEntryCollection.Clear();
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
            foreach (var kvp in statementDraftEntryCollection)
                draftEntryMap[kvp.Key] = kvp.Value.Id;
            statementDraftEntryCollection.Clear();
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Report Favorites"));
        if (root.TryGetProperty("ReportFavorites", out var favsEl) && favsEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Reports.ReportFavorite.ReportFavoriteBackupDto>>(favsEl.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Reports.ReportFavorite.ReportFavoriteBackupDto>();
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: ReportFavorite create - BackupId={BackupId}, Name={Name}", dto.Id, dto.Name);
                var entity = new ReportFavorite(userId, dto.Name, dto.PostingKind, dto.IncludeCategory, dto.Interval, dto.ComparePrevious, dto.CompareYear, dto.ShowChart, dto.Expandable, dto.Take);
                entity.AssignBackupDto(dto);
                _db.ReportFavorites.Add(entity);
                reportMap[dto.Id] = entity.Id;
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Home KPI"));
        if (root.TryGetProperty("HomeKpis", out var kpisEl) && kpisEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Reports.HomeKpi.HomeKpiBackupDto>>(kpisEl.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Reports.HomeKpi.HomeKpiBackupDto>();
            foreach (var dto in list)
            {
                Guid? favId = null;
                if (dto.ReportFavoriteId.HasValue && reportMap.TryGetValue(dto.ReportFavoriteId.Value, out var rf)) favId = rf;
                var entity = new HomeKpi(userId, dto.Kind, dto.DisplayMode, dto.SortOrder, favId);
                if (!string.IsNullOrWhiteSpace(dto.Title)) entity.SetTitle(dto.Title);
                if (dto.PredefinedType.HasValue) entity.SetPredefined(dto.PredefinedType.Value);
                if (dto.Id != Guid.Empty) _db.Entry(entity).Property("Id").CurrentValue = dto.Id;
                if (dto.CreatedUtc != default) _db.Entry(entity).Property("CreatedUtc").CurrentValue = DateTime.SpecifyKind(dto.CreatedUtc, DateTimeKind.Utc);
                if (dto.ModifiedUtc.HasValue) _db.Entry(entity).Property("ModifiedUtc").CurrentValue = DateTime.SpecifyKind(dto.ModifiedUtc.Value, DateTimeKind.Utc);
                _db.HomeKpis.Add(entity);
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // AttachmentCategories (create) - must be created before attachments to satisfy FK constraints
        ProgressChanged?.Invoke(this, progress.SetDescription("Attachment Categories"));
        if (root.TryGetProperty("AttachmentCategories", out var attCatsEl) && attCatsEl.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Attachments.AttachmentCategory.AttachmentCategoryBackupDto>>(attCatsEl.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Attachments.AttachmentCategory.AttachmentCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) { progress.IncSub(); continue; }
                var entity = new FinanceManager.Domain.Attachments.AttachmentCategory(userId, dto.Name);
                // apply backup metadata early so Id/CreatedUtc/ModifiedUtc/SymbolAttachmentId are preserved
                entity.AssignBackupDto(dto);
                _db.AttachmentCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                attachmentCatMap[dto.Id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Attachments
        ProgressChanged?.Invoke(this, progress.SetDescription("Attachments"));
        var attachmentMap = new Dictionary<Guid, Guid>();
        Queue<AttachmentBackupDto> references = new Queue<AttachmentBackupDto>();
        if (root.TryGetProperty("Attachments", out var attsEl) && attsEl.ValueKind == JsonValueKind.Array)
        {
            var attDtos = JsonSerializer.Deserialize<List<FinanceManager.Domain.Attachments.Attachment.AttachmentBackupDto>>(attsEl.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Attachments.Attachment.AttachmentBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(attDtos.Count));
            var processAttachmentDto = new Action<AttachmentBackupDto>((dto) =>
            {
                try
                {
                    Guid targetEntityId = dto.EntityId;
                    switch (dto.EntityKind)
                    {
                        case AttachmentEntityKind.ContactCategory:
                            if (contactCatMap.TryGetValue(dto.EntityId, out var mcc)) targetEntityId = mcc;
                            break;
                        case AttachmentEntityKind.Contact:
                            if (contactMap.TryGetValue(dto.EntityId, out var mc)) targetEntityId = mc;
                            break;
                        case AttachmentEntityKind.SecurityCategory:
                            if (securityCatMap.TryGetValue(dto.EntityId, out var msc)) targetEntityId = msc;
                            break;
                        case AttachmentEntityKind.Security:
                            if (securityMap.TryGetValue(dto.EntityId, out var ms)) targetEntityId = ms;
                            break;
                        case AttachmentEntityKind.SavingsPlanCategory:
                            if (savingsCatMap.TryGetValue(dto.EntityId, out var mspc)) targetEntityId = mspc;
                            break;
                        case AttachmentEntityKind.SavingsPlan:
                            if (savingsMap.TryGetValue(dto.EntityId, out var msp)) targetEntityId = msp;
                            break;
                        case AttachmentEntityKind.Account:
                            if (accountMap.TryGetValue(dto.EntityId, out var ma)) targetEntityId = ma;
                            break;
                        case AttachmentEntityKind.StatementDraft:
                            if (draftMap.TryGetValue(dto.EntityId, out var md)) targetEntityId = md;
                            break;
                        default:
                            break;
                    }

                    // map category id to newly created attachment category if present to avoid FK violations
                    Guid? mappedCategory = null;
                    if (dto.CategoryId.HasValue && attachmentCatMap.TryGetValue(dto.CategoryId.Value, out var mcat)) mappedCategory = mcat;
                    else mappedCategory = dto.CategoryId;

                    var att = new FinanceManager.Domain.Attachments.Attachment(userId, dto.EntityKind, targetEntityId, dto.FileName, dto.ContentType, dto.SizeBytes, dto.Sha256, mappedCategory, dto.Content, dto.Url, dto.ReferenceAttachmentId, dto.Role);
                    _db.Attachments.Add(att);
                    _db.SaveChanges();
                    attachmentMap[dto.Id] = att.Id;
                }
                catch (Exception ex)
                {
                    // ignore individual attachment failures
                    _logger.LogWarning("Failed to import attachment {AttachmentId}: {Message}", dto.Id, ex.Message);
                }
            });
            foreach (var dto in attDtos)
            {
                if (dto.ReferenceAttachmentId is not null)
                    references.Enqueue(dto);
                else
                {
                    processAttachmentDto(dto);
                    ProgressChanged?.Invoke(this, progress.IncSub());
                }
            }
            while (references.TryDequeue(out var dto))
            {
                if (!attachmentMap.TryGetValue(dto.ReferenceAttachmentId!.Value, out var refId))
                    continue;
                var exists = await _db.Attachments.AnyAsync(a => a.Id == refId, ct);
                if (exists)
                {
                    dto = dto with { ReferenceAttachmentId = refId };
                    processAttachmentDto(dto);
                }
                else
                    _logger.LogWarning("Failed to import attachment {AttachmentId}: referenced attachment {ReferenceAttachmentId} does not exist", dto.Id, dto.ReferenceAttachmentId);
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Remap symbol attachment ids in DTOs and apply AssignBackupDto on created entities
        ProgressChanged?.Invoke(this, progress.SetDescription("Apply backup DTOs"));

        // ContactCategories
        if (root.TryGetProperty("ContactCategories", out var contactCategoriesApply) && contactCategoriesApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.ContactCategory.ContactCategoryBackupDto>>(contactCategoriesApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.ContactCategory.ContactCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                _logger.LogDebug("ImportVersion3: ContactCategories create - BackupId={BackupId}, Name={Name}, SymbolAttachmentId={SymbolAttachmentId}", dto.Id, dto.Name, dto.SymbolAttachmentId);
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue)
                    if (attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt))
                        adjusted = dto with { SymbolAttachmentId = newAtt };
                    else
                        adjusted = dto with { SymbolAttachmentId = null };
                if (contactCatMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.ContactCategories.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // AliasNames (apply)
        if (root.TryGetProperty("AliasNames", out var aliasNamesApply) && aliasNamesApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.AliasName.AliasNameBackupDto>>(aliasNamesApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.AliasName.AliasNameBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (contactMap.TryGetValue(dto.ContactId, out var mappedContact)) adjusted = adjusted with { ContactId = mappedContact };
                if (aliasMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.AliasNames.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // SecurityCategories
        if (root.TryGetProperty("SecurityCategories", out var secCatsApply) && secCatsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.SecurityCategory.SecurityCategoryBackupDto>>(secCatsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.SecurityCategory.SecurityCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = dto with { SymbolAttachmentId = newAtt };
                if (securityCatMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.SecurityCategories.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // SavingsPlanCategories
        if (root.TryGetProperty("SavingsPlanCategories", out var spCatsApply) && spCatsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Savings.SavingsPlanCategory.SavingsPlanCategoryBackupDto>>(spCatsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Savings.SavingsPlanCategory.SavingsPlanCategoryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = dto with { SymbolAttachmentId = newAtt };
                if (savingsCatMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.SavingsPlanCategories.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // Securities
        if (root.TryGetProperty("Securities", out var secsApply) && secsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.Security.SecurityBackupDto>>(secsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.Security.SecurityBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = adjusted with { SymbolAttachmentId = newAtt };
                if (dto.CategoryId.HasValue && securityCatMap.TryGetValue(dto.CategoryId.Value, out var mappedCat)) adjusted = adjusted with { CategoryId = mappedCat };

                if (securityMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.Securities.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // SecurityPrices (apply) - remap SecurityId and apply backup DTOs to created entities
        if (root.TryGetProperty("SecurityPrices", out var secPricesApply) && secPricesApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Securities.SecurityPrice.SecurityPriceBackupDto>>(secPricesApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Securities.SecurityPrice.SecurityPriceBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                try
                {
                    var adjusted = dto;
                    if (dto.SecurityId != Guid.Empty && securityMap.TryGetValue(dto.SecurityId, out var mappedSec)) adjusted = adjusted with { SecurityId = mappedSec };

                    if (securityPricesMap.TryGetValue(dto.Id, out var mappedPriceId))
                    {
                        var priceEntity = await _db.SecurityPrices.FirstAsync(x => x.Id == mappedPriceId, ct);
                        priceEntity.AssignBackupDto(adjusted);
                        if (progress.SubStep % 100 == 0)
                            await _db.SaveChangesAsync(ct);
                    }
                }
                catch
                {
                    // ignore individual failures to keep apply resilient
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SavingsPlans
        if (root.TryGetProperty("SavingsPlans", out var spsApply) && spsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Savings.SavingsPlan.SavingsPlanBackupDto>>(spsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Savings.SavingsPlan.SavingsPlanBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = dto with { SymbolAttachmentId = newAtt };
                if (dto.CategoryId.HasValue && savingsCatMap.TryGetValue(dto.CategoryId.Value, out var mappedCat)) adjusted = adjusted with { CategoryId = mappedCat };

                if (savingsMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.SavingsPlans.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Accounts
        if (root.TryGetProperty("Accounts", out var accApply) && accApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Accounts.Account.AccountBackupDto>>(accApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Accounts.Account.AccountBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = adjusted with { SymbolAttachmentId = newAtt };
                if (!dto.BankContactId.Equals(Guid.Empty) && contactMap.TryGetValue(dto.BankContactId, out var mbc)) adjusted = adjusted with { BankContactId = mbc };
                else
                {
                    var bank = _db.Contacts.FirstOrDefault(c => c.Type == ContactType.Bank && c.Name == dto.Name);
                    if (bank is null)
                    {
                        _logger.LogWarning("Failed to import account {AccountId}: no matching bank contact found for name {Name}", dto.Id, dto.Name);
                        ProgressChanged?.Invoke(this, progress.IncSub());
                        continue;
                    }
                    adjusted = adjusted with { BankContactId = bank.Id };
                }
                if (accountMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.Accounts.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // Contacts
        if (root.TryGetProperty("Contacts", out var contactsApply) && contactsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Contacts.Contact.ContactBackupDto>>(contactsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Contacts.Contact.ContactBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                // remap symbol attachment id
                if (dto.SymbolAttachmentId.HasValue && attachmentMap.TryGetValue(dto.SymbolAttachmentId.Value, out var newAtt)) adjusted = adjusted with { SymbolAttachmentId = newAtt };
                // remap category id to newly created contact category id (avoid FK violation)
                if (dto.CategoryId.HasValue && contactCatMap.TryGetValue(dto.CategoryId.Value, out var mappedCat)) adjusted = adjusted with { CategoryId = mappedCat };
                if (contactMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.Contacts.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Postings (apply)
        if (root.TryGetProperty("Postings", out var postApply) && postApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Postings.Posting.PostingBackupDto>>(postApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Postings.Posting.PostingBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                Guid? accountId = dto.AccountId.HasValue && accountMap.TryGetValue(dto.AccountId.Value, out var a) ? a : null;
                Guid? contactId = dto.ContactId.HasValue && contactMap.TryGetValue(dto.ContactId.Value, out var c) ? c : null;
                Guid? savingsPlanId = dto.SavingsPlanId.HasValue && savingsMap.TryGetValue(dto.SavingsPlanId.Value, out var s) ? s : null;
                Guid? securityId = dto.SecurityId.HasValue && securityMap.TryGetValue(dto.SecurityId.Value, out var sd) ? sd : null;
                adjusted = adjusted with { AccountId = accountId, ContactId = contactId, SavingsPlanId = savingsPlanId, SecurityId = securityId };

                if (postingMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.Postings.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted);
                    if (progress.SubStep % 100 == 0)
                        await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        // StatementDrafts (apply) - preserve entries that are stored separately in the backup
        if (root.TryGetProperty("StatementDrafts", out var draftsApply) && draftsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Statements.StatementDraft.StatementDraftBackupDto>>(draftsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Statements.StatementDraft.StatementDraftBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                var adjusted = dto;
                if (dto.DetectedAccountId.HasValue && accountMap.TryGetValue(dto.DetectedAccountId.Value, out var mappedAccount)) adjusted = adjusted with { DetectedAccountId = mappedAccount };

                if (draftMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.StatementDrafts.FirstAsync(x => x.Id == mappedId, ct);
                    entity.AssignBackupDto(adjusted, false);
                    await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());


        if (root.TryGetProperty("StatementDraftEntries", out var draftEntriesApply) && draftEntriesApply.ValueKind == JsonValueKind.Array)
        {
            var listEntries = JsonSerializer.Deserialize<List<FinanceManager.Domain.Statements.StatementDraftEntry.StatementDraftEntryBackupDto>>(draftEntriesApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Statements.StatementDraftEntry.StatementDraftEntryBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(listEntries.Count));
            foreach (var dto in listEntries)
            {
                var adjusted = dto;
                if (draftMap.TryGetValue(dto.DraftId, out var mappedDraft)) adjusted = adjusted with { DraftId = mappedDraft };
                if (dto.ContactId.HasValue && contactMap.TryGetValue(dto.ContactId.Value, out var mappedC)) adjusted = adjusted with { ContactId = mappedC };
                if (dto.SavingsPlanId.HasValue && savingsMap.TryGetValue(dto.SavingsPlanId.Value, out var mappedS)) adjusted = adjusted with { SavingsPlanId = mappedS };
                if (dto.SplitDraftId.HasValue && draftMap.TryGetValue(dto.SplitDraftId.Value, out var mappedSplit)) adjusted = adjusted with { SplitDraftId = mappedSplit };
                if (dto.SecurityId.HasValue && securityMap.TryGetValue(dto.SecurityId.Value, out var mappedSec)) adjusted = adjusted with { SecurityId = mappedSec };


                if (draftEntryMap.TryGetValue(dto.Id, out var mappedId))
                {
                    var entity = await _db.StatementDraftEntries.FirstAsync(x => x.Id == mappedId);
                    entity.AssignBackupDto(adjusted);
                    if (progress.SubStep % 100 == 0)
                        await _db.SaveChangesAsync(ct);
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Attachments(apply): remap EntityId and ReferenceAttachmentId and apply DTOs to persisted Attachment entities
        if (root.TryGetProperty("Attachments", out var attsApply) && attsApply.ValueKind == JsonValueKind.Array)
        {
            var list = JsonSerializer.Deserialize<List<FinanceManager.Domain.Attachments.Attachment.AttachmentBackupDto>>(attsApply.GetRawText(), jsonOptions) ?? new List<FinanceManager.Domain.Attachments.Attachment.AttachmentBackupDto>();
            ProgressChanged?.Invoke(this, progress.InitSub(list.Count));
            foreach (var dto in list)
            {
                try
                {
                    var adjusted = dto;
                    // remap entity id targets using maps built earlier
                    switch (dto.EntityKind)
                    {
                        case AttachmentEntityKind.ContactCategory:
                            if (contactCatMap.TryGetValue(dto.EntityId, out var mcc)) adjusted = adjusted with { EntityId = mcc };
                            break;
                        case AttachmentEntityKind.Contact:
                            if (contactMap.TryGetValue(dto.EntityId, out var mc)) adjusted = adjusted with { EntityId = mc };
                            break;
                        case AttachmentEntityKind.SecurityCategory:
                            if (securityCatMap.TryGetValue(dto.EntityId, out var msc)) adjusted = adjusted with { EntityId = msc };
                            break;
                        case AttachmentEntityKind.Security:
                            if (securityMap.TryGetValue(dto.EntityId, out var ms)) adjusted = adjusted with { EntityId = ms };
                            break;
                        case AttachmentEntityKind.SavingsPlanCategory:
                            if (savingsCatMap.TryGetValue(dto.EntityId, out var mspc)) adjusted = adjusted with { EntityId = mspc };
                            break;
                        case AttachmentEntityKind.SavingsPlan:
                            if (savingsMap.TryGetValue(dto.EntityId, out var msp)) adjusted = adjusted with { EntityId = msp };
                            break;
                        case AttachmentEntityKind.Account:
                            if (accountMap.TryGetValue(dto.EntityId, out var ma)) adjusted = adjusted with { EntityId = ma };
                            break;
                        case AttachmentEntityKind.StatementDraft:
                            if (draftMap.TryGetValue(dto.EntityId, out var md)) adjusted = adjusted with { EntityId = md };
                            break;
                        default:
                            break;
                    }

                    // remap reference attachment id
                    if (dto.ReferenceAttachmentId.HasValue && attachmentMap.TryGetValue(dto.ReferenceAttachmentId.Value, out var newRef))
                        adjusted = adjusted with { ReferenceAttachmentId = newRef };

                    // remap category id on apply
                    if (dto.CategoryId.HasValue && attachmentCatMap.TryGetValue(dto.CategoryId.Value, out var mappedCat)) adjusted = adjusted with { CategoryId = mappedCat };

                    // locate created attachment entity and apply DTO
                    if (attachmentMap.TryGetValue(dto.Id, out var mappedId))
                    {
                        var entity = await _db.Attachments.FirstAsync(x => x.Id == mappedId, ct);
                        entity.AssignBackupDto(adjusted);
                        if (progress.SubStep % 100 == 0)
                            await _db.SaveChangesAsync(ct);
                    }
                }
                catch
                {
                    // ignore individual attachment apply failures to keep overall import resilient
                }
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }

        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Build Aggregate Postings"));
        ProgressChanged?.Invoke(this, progress.InitSub(postingCount));
        await _aggregateService.RebuildForUserAsync(userId, (step, count) =>
        {
            progress.SubStep = step;
            progress.SubTotal = count;
            ProgressChanged?.Invoke(this, progress);
        }, ct);
        ProgressChanged?.Invoke(this, progress.Inc());
    }

    private static DateTime GetPeriodStart(DateTime date, AggregatePeriod p)
    {
        var d = date.Date;
        return p switch
        {
            AggregatePeriod.Month => new DateTime(d.Year, d.Month, 1),
            AggregatePeriod.Quarter => new DateTime(d.Year, ((d.Month - 1) / 3) * 3 + 1, 1),
            AggregatePeriod.HalfYear => new DateTime(d.Year, (d.Month <= 6 ? 1 : 7), 1),
            AggregatePeriod.Year => new DateTime(d.Year, 1, 1),
            _ => new DateTime(d.Year, d.Month, 1)
        };
    }

    private async Task RebuildAggregatesForUserAsync(Guid userId, CancellationToken ct)
    {
        // Collect owned entity IDs
        var accountIds = await _db.Accounts.AsNoTracking().Where(a => a.OwnerUserId == userId).Select(a => a.Id).ToListAsync(ct);
        var contactIds = await _db.Contacts.AsNoTracking().Where(c => c.OwnerUserId == userId).Select(c => c.Id).ToListAsync(ct);
        var savingsPlanIds = await _db.SavingsPlans.AsNoTracking().Where(s => s.OwnerUserId == userId).Select(s => s.Id).ToListAsync(ct);
        var securityIds = await _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == userId).Select(s => s.Id).ToListAsync(ct);

        // Delete existing aggregates for this user's scope
        var aggsToDelete = _db.PostingAggregates
            .Where(p => (p.AccountId != null && accountIds.Contains(p.AccountId.Value))
                     || (p.ContactId != null && contactIds.Contains(p.ContactId.Value))
                     || (p.SavingsPlanId != null && savingsPlanIds.Contains(p.SavingsPlanId.Value))
                     || (p.SecurityId != null && securityIds.Contains(p.SecurityId.Value)));
        _db.PostingAggregates.RemoveRange(aggsToDelete);
        await _db.SaveChangesAsync(ct);

        // Load postings belonging to this user
        var postings = await _db.Postings.AsNoTracking()
            .Where(p => (p.AccountId != null && accountIds.Contains(p.AccountId.Value))
                     || (p.ContactId != null && contactIds.Contains(p.ContactId.Value))
                     || (p.SavingsPlanId != null && savingsPlanIds.Contains(p.SavingsPlanId.Value))
                     || (p.SecurityId != null && securityIds.Contains(p.SecurityId.Value)))
            .Select(p => new
            {
                p.Kind,
                p.AccountId,
                p.ContactId,
                p.SavingsPlanId,
                p.SecurityId,
                p.BookingDate,
                p.Amount
            })
            .ToListAsync(ct);

        // Upsert aggregates in memory; rely on unique indexes for consistency across saves
        var periods = new[] { AggregatePeriod.Month, AggregatePeriod.Quarter, AggregatePeriod.HalfYear, AggregatePeriod.Year };
        foreach (var p in postings)
        {
            if (p.Amount == 0m) { continue; }
            foreach (var period in periods)
            {
                var periodStart = GetPeriodStart(p.BookingDate, period);

                async Task Upsert(Guid? accountId, Guid? contactId, Guid? savingsPlanId, Guid? securityId)
                {
                    var agg = _db.PostingAggregates.Local.FirstOrDefault(x => x.Kind == p.Kind
                        && x.AccountId == accountId
                        && x.ContactId == contactId
                        && x.SavingsPlanId == savingsPlanId
                        && x.SecurityId == securityId
                        && x.Period == period
                        && x.PeriodStart == periodStart);

                    if (agg == null)
                    {
                        agg = await _db.PostingAggregates
                            .FirstOrDefaultAsync(x => x.Kind == p.Kind
                                && x.AccountId == accountId
                                && x.ContactId == contactId
                                && x.SavingsPlanId == savingsPlanId
                                && x.SecurityId == securityId
                                && x.Period == period
                                && x.PeriodStart == periodStart, ct);
                    }

                    if (agg == null)
                    {
                        agg = new PostingAggregate(p.Kind, accountId, contactId, savingsPlanId, securityId, periodStart, period);
                        _db.PostingAggregates.Add(agg);
                    }
                    agg.Add(p.Amount);
                }

                switch (p.Kind)
                {
                    case PostingKind.Bank:
                        await Upsert(p.AccountId, null, null, null);
                        break;
                    case PostingKind.Contact:
                        await Upsert(null, p.ContactId, null, null);
                        break;
                    case PostingKind.SavingsPlan:
                        await Upsert(null, null, p.SavingsPlanId, null);
                        break;
                    case PostingKind.Security:
                        await Upsert(null, null, null, p.SecurityId);
                        break;
                    default:
                        break;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private sealed class BackupMeta
    {
        public string Type { get; set; } = string.Empty;
        public int Version { get; set; }
    }
}