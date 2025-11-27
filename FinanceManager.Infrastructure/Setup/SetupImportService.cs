using FinanceManager.Application.Statements;
using FinanceManager.Domain; // for enums like AccountType, PostingKind
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Savings;
using FinanceManager.Domain.Securities;
using FinanceManager.Domain.Statements; // for StatementDraft
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using FinanceManager.Domain.Postings; // AggregatePeriod, PostingAggregate
using FinanceManager.Application.Aggregates;
using FinanceManager.Domain.Reports;
using FinanceManager.Application.Attachments; // new
using FinanceManager.Domain.Attachments; // new

public sealed class SetupImportService : ISetupImportService
{
    private readonly AppDbContext _db;
    private readonly IStatementDraftService _statementDraftService;
    private readonly IPostingAggregateService _aggregateService;
    private readonly IAttachmentService _attachments; // new

    public SetupImportService(AppDbContext db, IStatementDraftService statementDraftService, IPostingAggregateService aggregateService, IAttachmentService? attachments = null)
    {
        _db = db;
        _statementDraftService = statementDraftService;
        _aggregateService = aggregateService;
        _attachments = attachments ?? new NoopAttachmentService();
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

    public struct ImportProgress
    {
        public int Step { get; set; }
        public int Total { get; set; }

        public int SubStep { get; set; }
        public int SubTotal { get; set; }
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
    public event EventHandler<ImportProgress> ProgressChanged;

    public async Task ImportAsync(Guid userId, Stream fileStream, bool replaceExisting, CancellationToken ct)
    {
        using var reader = new StreamReader(fileStream, Encoding.UTF8);

        // Erste Zeile: Metadaten-Objekt
        var metaLine = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(metaLine))
            throw new InvalidOperationException("Backup-Metadaten fehlen.");

        var meta = JsonSerializer.Deserialize<BackupMeta>(metaLine);
        if (meta == null || meta.Type != "Backup" || meta.Version < 2)
            throw new InvalidOperationException("Ungültiges Backup-Format.");
        var jsonData = await reader.ReadToEndAsync();
        switch (meta.Version)
        {
            case 2:
                await ImportVersion2(jsonData, replaceExisting, userId, meta, ct);
                break;
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
            Total = replaceExisting ? 16 : 15,
            SubStep = 0,
            SubTotal = 0
        };
        ProgressChanged?.Invoke(this, progress);

        if (replaceExisting)
        {
            ProgressChanged?.Invoke(this, progress.SetDescription("Clearing user data"));
            await _db.ClearUserDataAsync(userId, (step, count) => {
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
        var savingsCatMap = new Dictionary<Guid, Guid>();
        var savingsMap = new Dictionary<Guid, Guid>();
        var accountMap = new Dictionary<Guid, Guid>();
        var draftMap = new Dictionary<Guid, Guid>();
        var favoriteMap = new Dictionary<Guid, Guid>();
        var reportMap = new Dictionary<Guid, Guid>();

        // ContactCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Contact Categories"));
        if (root.TryGetProperty("ContactCategories", out var contactCategories) && contactCategories.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(contactCategories.GetArrayLength()));
            foreach (var c in contactCategories.EnumerateArray())
            {
                var id = c.GetProperty("Id").GetGuid();
                var name = c.GetProperty("Name").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name)) { continue; }
                var entity = new ContactCategory(userId, name);
                _db.ContactCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                contactCatMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Contacts
        ProgressChanged?.Invoke(this, progress.SetDescription("Contacts"));
        if (root.TryGetProperty("Contacts", out var contacts) && contacts.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(contacts.GetArrayLength()));
            foreach (var c in contacts.EnumerateArray())
            {
                var id = c.GetProperty("Id").GetGuid();
                var name = c.GetProperty("Name").GetString() ?? string.Empty;
                var type = (ContactType)c.GetProperty("Type").GetInt32();
                Guid? categoryId = null;
                if (c.TryGetProperty("CategoryId", out var cat) && cat.ValueKind == JsonValueKind.String)
                {
                    var old = cat.GetGuid();
                    if (contactCatMap.TryGetValue(old, out var mapped)) { categoryId = mapped; }
                }
                var description = c.TryGetProperty("Description", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() : null;
                var isIntermediary = c.TryGetProperty("IsPaymentIntermediary", out var inter) && inter.ValueKind == JsonValueKind.True;
                var entity = new Contact(userId, name, type, categoryId, description, isIntermediary);
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
                contactMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // AliasNames
        ProgressChanged?.Invoke(this, progress.SetDescription("Contact Alias Names"));
        if (root.TryGetProperty("AliasNames", out var aliases) && aliases.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(aliases.GetArrayLength()));
            foreach (var a in aliases.EnumerateArray())
            {
                var id = a.GetProperty("Id").GetGuid();
                var pattern = a.GetProperty("Pattern").GetString() ?? string.Empty;
                var contactId = a.TryGetProperty("ContactId", out var cid) && cid.ValueKind == JsonValueKind.String ? cid.GetGuid() : Guid.Empty;
                if (!contactMap.TryGetValue(contactId, out var mappedContact)) { continue; }
                var entity = new AliasName(mappedContact, pattern);
                _db.AliasNames.Add(entity);
                await _db.SaveChangesAsync(ct);
                aliasMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SecurityCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Security Categories"));
        if (root.TryGetProperty("SecurityCategories", out var secCats) && secCats.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(secCats.GetArrayLength()));
            foreach (var sc in secCats.EnumerateArray())
            {
                var id = sc.GetProperty("Id").GetGuid();
                var name = sc.GetProperty("Name").GetString() ?? string.Empty;
                var entity = new SecurityCategory(userId, name);
                _db.SecurityCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                securityCatMap[id] = entity.Id;
            }
            ProgressChanged?.Invoke(this, progress.IncSub());
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Securities
        ProgressChanged?.Invoke(this, progress.SetDescription("Securities"));
        if (root.TryGetProperty("Securities", out var secs) && secs.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(secs.GetArrayLength()));
            foreach (var s in secs.EnumerateArray())
            {
                var id = s.GetProperty("Id").GetGuid();
                var name = s.GetProperty("Name").GetString() ?? string.Empty;
                var identifier = s.GetProperty("Identifier").GetString() ?? string.Empty;
                var description = s.TryGetProperty("Description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var av = s.TryGetProperty("AlphaVantageCode", out var avc) && avc.ValueKind == JsonValueKind.String ? avc.GetString() : null;
                var cur = s.GetProperty("CurrencyCode").GetString() ?? "EUR";
                Guid? categoryId = null;
                if (s.TryGetProperty("CategoryId", out var scid) && scid.ValueKind == JsonValueKind.String)
                {
                    var old = scid.GetGuid();
                    if (securityCatMap.TryGetValue(old, out var mapped)) { categoryId = mapped; }
                }
                var entity = new Security(userId, name, identifier, description, av, cur, categoryId);
                _db.Securities.Add(entity);
                await _db.SaveChangesAsync(ct);
                securityMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SecurityPrices
        ProgressChanged?.Invoke(this, progress.SetDescription("Security Prices"));
        if (root.TryGetProperty("SecurityPrices", out var prices) && prices.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(prices.GetArrayLength()));
            foreach (var p in prices.EnumerateArray())
            {
                var sid = p.GetProperty("SecurityId").GetGuid();
                if (!securityMap.TryGetValue(sid, out var mappedSid)) { continue; }
                var date = p.GetProperty("Date").GetDateTime();
                var close = p.GetProperty("Close").GetDecimal();
                var entity = new SecurityPrice(mappedSid, date, close);
                _db.SecurityPrices.Add(entity);
                if (progress.SubStep % 100 == 0)
                    await _db.SaveChangesAsync(ct);
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SavingsPlanCategories
        ProgressChanged?.Invoke(this, progress.SetDescription("Savings Plan Categories"));
        if (root.TryGetProperty("SavingsPlanCategories", out var spCats) && spCats.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(spCats.GetArrayLength()));
            foreach (var sc in spCats.EnumerateArray())
            {
                var id = sc.GetProperty("Id").GetGuid();
                var name = sc.GetProperty("Name").GetString() ?? string.Empty;
                var entity = new SavingsPlanCategory(userId, name);
                _db.SavingsPlanCategories.Add(entity);
                await _db.SaveChangesAsync(ct);
                savingsCatMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // SavingsPlans
        ProgressChanged?.Invoke(this, progress.SetDescription("Savings Plans"));
        if (root.TryGetProperty("SavingsPlans", out var sps) && sps.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(sps.GetArrayLength()));
            foreach (var sp in sps.EnumerateArray())
            {
                var id = sp.GetProperty("Id").GetGuid();
                var name = sp.GetProperty("Name").GetString() ?? string.Empty;
                var type = (SavingsPlanType)sp.GetProperty("Type").GetInt32();
                var targetAmount = sp.TryGetProperty("TargetAmount", out var ta) && ta.ValueKind != JsonValueKind.Null ? ta.GetDecimal() : (decimal?)null;
                var targetDate = sp.TryGetProperty("TargetDate", out var td) && td.ValueKind != JsonValueKind.Null ? td.GetDateTime() : (DateTime?)null;
                var interval = sp.TryGetProperty("Interval", out var iv) && iv.ValueKind != JsonValueKind.Null ? (SavingsPlanInterval?)iv.GetInt32() : null;
                Guid? categoryId = null;
                if (sp.TryGetProperty("CategoryId", out var cid) && cid.ValueKind == JsonValueKind.String)
                {
                    var old = cid.GetGuid();
                    if (savingsCatMap.TryGetValue(old, out var mapped)) { categoryId = mapped; }
                }
                var entity = new SavingsPlan(userId, name, type, targetAmount, targetDate, interval, categoryId);
                if (sp.TryGetProperty("ContractNumber", out var cn) && cn.ValueKind == JsonValueKind.String)
                {
                    entity.SetContractNumber(cn.GetString());
                }
                _db.SavingsPlans.Add(entity);
                await _db.SaveChangesAsync(ct);
                savingsMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Accounts
        ProgressChanged?.Invoke(this, progress.SetDescription("Bank Accounts"));
        if (root.TryGetProperty("Accounts", out var accounts) && accounts.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(accounts.GetArrayLength()));
            foreach (var a in accounts.EnumerateArray())
            {
                var id = a.GetProperty("Id").GetGuid();
                var name = a.GetProperty("Name").GetString() ?? string.Empty;
                var type = (AccountType)a.GetProperty("Type").GetInt32();
                var iban = a.TryGetProperty("Iban", out var ib) && ib.ValueKind == JsonValueKind.String ? ib.GetString() : null;
                var bankContactId = a.TryGetProperty("BankContactId", out var bc) && bc.ValueKind == JsonValueKind.String ? bc.GetGuid() : Guid.Empty;
                if (!contactMap.TryGetValue(bankContactId, out var mappedBankContact))
                {
                    // Fallback: create bank contact
                    var bank = new Contact(userId, "Bank", ContactType.Bank, null);
                    _db.Contacts.Add(bank);
                    await _db.SaveChangesAsync(ct);
                    mappedBankContact = bank.Id;
                }
                var entity = new Account(userId, type, name, iban, mappedBankContact);
                _db.Accounts.Add(entity);
                await _db.SaveChangesAsync(ct);
                accountMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // Postings
        var postingCount = 0;
        ProgressChanged?.Invoke(this, progress.SetDescription("Postings"));
        if (root.TryGetProperty("Postings", out var postArr) && postArr.ValueKind == JsonValueKind.Array)
        {
            postingCount = postArr.GetArrayLength();
            ProgressChanged?.Invoke(this, progress.InitSub(postingCount));
            foreach (var p in postArr.EnumerateArray())
            {
                Guid? accountId = null, contactId = null, savingsPlanId = null, securityId = null;
                if (p.TryGetProperty("AccountId", out var aid) && aid.ValueKind == JsonValueKind.String)
                {
                    var old = aid.GetGuid(); if (accountMap.TryGetValue(old, out var mapped)) accountId = mapped;
                }
                if (p.TryGetProperty("ContactId", out var cid) && cid.ValueKind == JsonValueKind.String)
                {
                    var old = cid.GetGuid(); if (contactMap.TryGetValue(old, out var mapped)) contactId = mapped;
                }
                if (p.TryGetProperty("SavingsPlanId", out var spid) && spid.ValueKind == JsonValueKind.String)
                {
                    var old = spid.GetGuid(); if (savingsMap.TryGetValue(old, out var mapped)) savingsPlanId = mapped;
                }
                if (p.TryGetProperty("SecurityId", out var sid) && sid.ValueKind == JsonValueKind.String)
                {
                    var old = sid.GetGuid(); if (securityMap.TryGetValue(old, out var mapped)) securityId = mapped;
                }
                var kind = (PostingKind)p.GetProperty("Kind").GetInt32();
                var sourceId = p.TryGetProperty("SourceId", out var src) && src.ValueKind == JsonValueKind.String ? src.GetGuid() : Guid.NewGuid();
                var bookingDate = p.GetProperty("BookingDate").GetDateTime();
                var amount = p.GetProperty("Amount").GetDecimal();
                var subject = p.TryGetProperty("Subject", out var sub) && sub.ValueKind == JsonValueKind.String ? sub.GetString() : null;
                var recipient = p.TryGetProperty("RecipientName", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() : null;
                var description = p.TryGetProperty("Description", out var desc) && desc.ValueKind == JsonValueKind.String ? desc.GetString() : null;
                SecurityPostingSubType? subType = null;
                if (p.TryGetProperty("SecuritySubType", out var sst) && sst.ValueKind != JsonValueKind.Null)
                {
                    subType = (SecurityPostingSubType)sst.GetInt32();
                }
                decimal? quantity = null;
                if (p.TryGetProperty("Quantity", out var q) && q.ValueKind != JsonValueKind.Null)
                {
                    quantity = q.GetDecimal();
                }
                var entity = new FinanceManager.Domain.Postings.Posting(
                    sourceId,
                    kind,
                    accountId,
                    contactId,
                    savingsPlanId,
                    securityId,
                    bookingDate,
                    amount,
                    subject,
                    recipient,
                    description,
                    subType,
                    quantity);
                if (p.TryGetProperty("GroupId", out var gid) && gid.ValueKind == JsonValueKind.String)
                {
                    var grp = gid.GetGuid(); if (grp != Guid.Empty) entity.SetGroup(grp);
                }
                _db.Postings.Add(entity);
                if (progress.SubStep % 100 == 0)
                    await _db.SaveChangesAsync(ct);
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        // StatementDrafts + Entries
        ProgressChanged?.Invoke(this, progress.SetDescription("Statement Drafts"));
        if (root.TryGetProperty("StatementDrafts", out var drafts) && drafts.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(drafts.GetArrayLength()));
            foreach (var d in drafts.EnumerateArray())
            {
                var id = d.GetProperty("Id").GetGuid();
                var originalFileName = d.TryGetProperty("OriginalFileName", out var of) && of.ValueKind == JsonValueKind.String ? of.GetString() : "backup";
                var accountName = d.TryGetProperty("AccountName", out var an) && an.ValueKind == JsonValueKind.String ? an.GetString() : null;
                var description = d.TryGetProperty("Description", out var de) && de.ValueKind == JsonValueKind.String ? de.GetString() : null;
                var statusValue = d.TryGetProperty("Status", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : -1;
                var status = (StatementDraftStatus)statusValue;
                var entity = new StatementDraft(userId, originalFileName!, accountName, description, statusValue == -1 ? StatementDraftStatus.Draft: status);
                if (d.TryGetProperty("DetectedAccountId", out var da) && da.ValueKind == JsonValueKind.String)
                {
                    var old = da.GetGuid(); if (accountMap.TryGetValue(old, out var mapped)) entity.SetDetectedAccount(mapped);
                }
                // Legacy backup may contain embedded file bytes; upload them as attachment now
                if (d.TryGetProperty("OriginalFileContent", out var ofc) && ofc.ValueKind == JsonValueKind.Array)
                {
                    try
                    {
                        var bytes = ofc.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
                        var ctype = d.TryGetProperty("OriginalFileContentType", out var ctpe) && ctpe.ValueKind == JsonValueKind.String ? ctpe.GetString() : null;
                        if (bytes.Length > 0)
                        {
                            using var ms = new MemoryStream(bytes, writable: false);
                            await _db.StatementDrafts.AddAsync(entity, ct);
                            await _db.SaveChangesAsync(ct); // ensure entity.Id
                            await _attachments.UploadAsync(userId, AttachmentEntityKind.StatementDraft, entity.Id, ms, originalFileName!, ctype ?? "application/octet-stream", null, ct);
                        }
                    }
                    catch { }
                }
                if (_db.Entry(entity).State == EntityState.Detached)
                {
                    _db.StatementDrafts.Add(entity);
                }
                await _db.SaveChangesAsync(ct);
                draftMap[id] = entity.Id;
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Statement Draft Entries"));
        if (root.TryGetProperty("StatementDraftEntries", out var draftEntries) && draftEntries.ValueKind == JsonValueKind.Array)
        {
            ProgressChanged?.Invoke(this, progress.InitSub(draftEntries.GetArrayLength()));
            foreach (var e in draftEntries.EnumerateArray())
            {
                var draftIdOld = e.GetProperty("DraftId").GetGuid();
                if (!draftMap.TryGetValue(draftIdOld, out var draftId)) { continue; }
                var bookingDate = e.GetProperty("BookingDate").GetDateTime();
                var amount = e.GetProperty("Amount").GetDecimal();
                var subject = e.GetProperty("Subject").GetString() ?? string.Empty;
                var recipient = e.TryGetProperty("RecipientName", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() : null;
                var valuta = e.TryGetProperty("ValutaDate", out var vd) && vd.ValueKind != JsonValueKind.Null ? vd.GetDateTime() : (DateTime?)null;
                var currency = e.TryGetProperty("CurrencyCode", out var cc) && cc.ValueKind == JsonValueKind.String ? cc.GetString() : null;
                var bookingDesc = e.TryGetProperty("BookingDescription", out var bd) && bd.ValueKind == JsonValueKind.String ? bd.GetString() : null;
                var isAnnounced = e.TryGetProperty("IsAnnounced", out var ia) && ia.ValueKind == JsonValueKind.True;
                var isCostNeutral = e.TryGetProperty("IsCostNeutral", out var ic) && ic.ValueKind == JsonValueKind.True;
                var status = e.TryGetProperty("Status", out var st) && st.ValueKind == JsonValueKind.Number ? (StatementDraftEntryStatus)st.GetInt32(): isAnnounced ? StatementDraftEntryStatus.Announced : StatementDraftEntryStatus.Open;


                // Load draft entity
                var draft = await _db.StatementDrafts.FirstAsync(x => x.Id == draftId, ct);

                var entry = new StatementDraftEntry(
                    draft.Id,
                    bookingDate,
                    amount,
                    subject,
                    recipient,
                    valuta,
                    currency,
                    bookingDesc,
                    isAnnounced,
                    isCostNeutral,
                    status);
                _db.StatementDraftEntries.Add(entry);

                if (e.TryGetProperty("ContactId", out var cid) && cid.ValueKind == JsonValueKind.String)
                {
                    var old = cid.GetGuid(); if (contactMap.TryGetValue(old, out var mapped)) { entry.AssignContactWithoutAccounting(mapped); }
                }
                if (e.TryGetProperty("SavingsPlanId", out var spid) && spid.ValueKind == JsonValueKind.String)
                {
                    var old = spid.GetGuid(); if (savingsMap.TryGetValue(old, out var mapped)) { entry.AssignSavingsPlan(mapped); }
                }
                if (e.TryGetProperty("ArchiveSavingsPlanOnBooking", out var arch) && arch.ValueKind != JsonValueKind.Null && arch.GetBoolean())
                {
                    entry.SetArchiveSavingsPlanOnBooking(true);
                }
                if (e.TryGetProperty("SplitDraftId", out var sdid) && sdid.ValueKind == JsonValueKind.String)
                {
                    var old = sdid.GetGuid(); if (draftMap.TryGetValue(old, out var mapped)) { entry.AssignSplitDraft(mapped); }
                }
                if (e.TryGetProperty("SecurityId", out var secid) && secid.ValueKind == JsonValueKind.String)
                {
                    var old = secid.GetGuid();
                    Guid? mapped = null; if (securityMap.TryGetValue(old, out var m)) mapped = m;
                    SecurityTransactionType? tx = null; decimal? qty = null; decimal? fee = null; decimal? tax = null;
                    if (e.TryGetProperty("SecurityTransactionType", out var stt) && stt.ValueKind != JsonValueKind.Null) tx = (SecurityTransactionType)stt.GetInt32();
                    if (e.TryGetProperty("SecurityQuantity", out var q) && q.ValueKind != JsonValueKind.Null) qty = q.GetDecimal();
                    if (e.TryGetProperty("SecurityFeeAmount", out var f) && f.ValueKind != JsonValueKind.Null) fee = f.GetDecimal();
                    if (e.TryGetProperty("SecurityTaxAmount", out var t) && t.ValueKind != JsonValueKind.Null) tax = t.GetDecimal();
                    entry.SetSecurity(mapped, tx, qty, fee, tax);
                }
                if (progress.SubStep % 100 ==  0)
                    await _db.SaveChangesAsync(ct);
                ProgressChanged?.Invoke(this, progress.IncSub());
            }
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Report Favorites"));
        if (root.TryGetProperty("ReportFavorites", out var favsEl) && favsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in favsEl.EnumerateArray())
            {
                var id = f.GetProperty("Id").GetGuid();
                var name = f.GetProperty("Name").GetString() ?? string.Empty;
                var postingKind = (PostingKind)f.GetProperty("PostingKind").GetInt32();
                var includeCategory = f.GetProperty("IncludeCategory").GetBoolean();
                var interval = (ReportInterval)f.GetProperty("Interval").GetInt32();
                var take = f.TryGetProperty("Take", out var takeEl) ? takeEl.GetInt32() : 24;
                var comparePrev = f.GetProperty("ComparePrevious").GetBoolean();
                var compareYear = f.GetProperty("CompareYear").GetBoolean();
                var showChart = f.GetProperty("ShowChart").GetBoolean();
                var expandable = f.GetProperty("Expandable").GetBoolean();

                var entity = new ReportFavorite(userId, name, postingKind, includeCategory, interval, comparePrev, compareYear, showChart, expandable, take);

                // Optional multi kinds
                if (f.TryGetProperty("PostingKindsCsv", out var kindsEl))
                {
                    var kindsCsv = kindsEl.GetString();
                    if (!string.IsNullOrWhiteSpace(kindsCsv))
                    {
                        var kinds = kindsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                            .Where(v => v.HasValue).Select(v => (PostingKind)v!.Value).ToArray();
                        if (kinds.Length > 0) { entity.SetPostingKinds(kinds); }
                    }
                }

                // Optional filters (CSV -> GUID lists)
                string? accCsv = f.TryGetProperty("AccountIdsCsv", out var accEl) ? accEl.GetString() : null;
                string? conCsv = f.TryGetProperty("ContactIdsCsv", out var conEl) ? conEl.GetString() : null;
                string? savCsv = f.TryGetProperty("SavingsPlanIdsCsv", out var savEl) ? savEl.GetString() : null;
                string? secCsv = f.TryGetProperty("SecurityIdsCsv", out var secEl) ? secEl.GetString() : null;
                string? ccatCsv = f.TryGetProperty("ContactCategoryIdsCsv", out var ccatEl) ? ccatEl.GetString() : null;
                string? scatCsv = f.TryGetProperty("SavingsPlanCategoryIdsCsv", out var scatEl) ? scatEl.GetString() : null;
                string? secatCsv = f.TryGetProperty("SecurityCategoryIdsCsv", out var secatEl) ? secatEl.GetString() : null;

                static IReadOnlyCollection<Guid>? ToGuids(string? csv)
                    => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();

                entity.SetFilters(ToGuids(accCsv), ToGuids(conCsv), ToGuids(savCsv), ToGuids(secCsv), ToGuids(ccatCsv), ToGuids(scatCsv), ToGuids(secatCsv), null);

                // Optional: restore SecuritySubTypesCsv if present in backup v3
                if (f.TryGetProperty("SecuritySubTypesCsv", out var stCsvEl))
                {
                    var stCsv = stCsvEl.GetString();
                    if (!string.IsNullOrWhiteSpace(stCsv))
                    {
                        var ints = stCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                            .Where(v => v.HasValue).Select(v => v!.Value).ToArray();
                        if (ints.Length > 0)
                        {
                            _db.Entry(entity).Property("SecuritySubTypesCsv").CurrentValue = string.Join(',', ints);
                        }
                    }
                }

                _db.ReportFavorites.Add(entity);
                reportMap[id] = entity.Id;
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Home KPI"));
        if (root.TryGetProperty("HomeKpis", out var kpisEl) && kpisEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var k in kpisEl.EnumerateArray())
            {
                var kind = (HomeKpiKind)k.GetProperty("Kind").GetInt32();
                var display = (HomeKpiDisplayMode)k.GetProperty("DisplayMode").GetInt32();
                var sortOrder = k.GetProperty("SortOrder").GetInt32();
                Guid? favId = null;
                if (k.TryGetProperty("ReportFavoriteId", out var rfEl) && rfEl.ValueKind == JsonValueKind.String && Guid.TryParse(rfEl.GetString(), out var rf))
                {
                    favId = reportMap[rf];                    
                }
                var entity = new HomeKpi(userId, kind, display, sortOrder, favId);
                if (k.TryGetProperty("Title", out var tEl))
                {
                    entity.SetTitle(tEl.GetString());
                }
                if (k.TryGetProperty("PredefinedType", out var pEl) && pEl.ValueKind != JsonValueKind.Null)
                {
                    var pt = (HomeKpiPredefined)pEl.GetInt32();
                    entity.SetPredefined(pt);
                }
                if (k.TryGetProperty("Id", out var idEl) && idEl.ValueKind == JsonValueKind.String && Guid.TryParse(idEl.GetString(), out var id))
                {
                    _db.Entry(entity).Property("Id").CurrentValue = id;
                }
                if (k.TryGetProperty("CreatedUtc", out var cuEl) && cuEl.ValueKind == JsonValueKind.String && DateTime.TryParse(cuEl.GetString(), out var cu))
                {
                    _db.Entry(entity).Property("CreatedUtc").CurrentValue = DateTime.SpecifyKind(cu, DateTimeKind.Utc);
                }
                if (k.TryGetProperty("ModifiedUtc", out var muEl) && muEl.ValueKind == JsonValueKind.String && DateTime.TryParse(muEl.GetString(), out var mu))
                {
                    _db.Entry(entity).Property("ModifiedUtc").CurrentValue = DateTime.SpecifyKind(mu, DateTimeKind.Utc);
                }
                _db.HomeKpis.Add(entity);
            }
            await _db.SaveChangesAsync(ct);
        }
        ProgressChanged?.Invoke(this, progress.Inc());

        ProgressChanged?.Invoke(this, progress.SetDescription("Build Aggregate Postings"));
        ProgressChanged?.Invoke(this, progress.InitSub(postingCount));
        await _aggregateService.RebuildForUserAsync(userId, (step, count) => {
            progress.SubStep = step;
            progress.SubTotal = count;
            ProgressChanged?.Invoke(this, progress);
        }, ct);
        ProgressChanged?.Invoke(this, progress.Inc());

        await _db.SaveChangesAsync(ct);
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

    private async Task ImportVersion2(string jsonData, bool replaceExisting, Guid userId, BackupMeta meta, CancellationToken ct)
    {
        var backupData = JsonSerializer.Deserialize<BackupData>(jsonData);
        if (backupData == null)
            throw new InvalidOperationException("Backup-Daten konnten nicht gelesen werden.");
        if (replaceExisting)
        {
            await _db.ClearUserDataAsync(userId, (i1, i2) => { }, ct);
            await _db.SaveChangesAsync();
        }

        var contactCategories = ImportContactCategories(backupData.ContactCategories, userId).ToList();
        var contacts = ImportContacts(backupData.Contacts, contactCategories, userId).ToList();
        var accounts = ImportAccounts(backupData.BankAccounts, userId).ToList();
        var savingsPlanCategories = ImportSavingsPlanCategories(backupData.FixedAssetCategories, userId).ToList();
        var savingsPlans = ImportSavingsPlans(backupData.FixedAssets, savingsPlanCategories, userId).ToList();
        var stocks = ImportStocks(backupData.Stocks, userId).ToList();
        await _db.SaveChangesAsync();
        await ImportLedgerEntriesAsync(meta, backupData.BankAccounts, backupData.BankAccountLedgerEntries, backupData.StockLedgerEntries, stocks, backupData.FixedAssetLedgerEntries, savingsPlans, userId, contacts, ct);
        await _db.SaveChangesAsync();
    }

    private async Task ImportLedgerEntriesAsync(BackupMeta meta, JsonElement bankAccounts, BackupBankAccountLedgerEntry[]? bankAccountLedgerEntries, JsonElement stockLedgerEntries, List<KeyValuePair<int, Guid>> stocks, JsonElement fixedAssetLedgerEntries, List<KeyValuePair<int, Guid>> savingsPlans, Guid userId, List<KeyValuePair<int, Guid>> contacts, CancellationToken ct)
    {
        if (bankAccountLedgerEntries is null)
            return;
        // 1) Erste Zeile: Meta als JSON
        var metaJson = JsonSerializer.Serialize(meta);

        // 2) Zweite Zeile: Objekt mit minimalen Feldern, die der BackupStatementFileReader erwartet
        //    - BankAccounts: enthält mindestens eine IBAN (für Header-Detektion)
        //    - BankAccountLedgerEntries: übernommene Bewegungen
        //    - BankAccountJournalLines: leeres Array (Reader erwartet Feld)
        var firstIban = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OwnerUserId == userId && a.Iban != null && a.Iban != "")
            .Select(a => a.Iban!)
            .FirstOrDefaultAsync(ct) ?? string.Empty;
        var emptyArray = new string[0];
        var dataObj = new
        {
            BankAccounts = bankAccounts,
            BankAccountLedgerEntries = bankAccountLedgerEntries.Select(entry =>
            {
                if (entry.SourceContact is not null)
                    entry.SourceContact.UID = contacts.FirstOrDefault(c => c.Key == entry.SourceContact?.Id).Value;                
                var stockEntries = stockLedgerEntries.EnumerateArray().Where(e =>
                {
                    return e.GetProperty("SourceLedgerEntry").GetProperty("Id").GetInt32() == entry.Id;
                }).ToArray();
                if (stockEntries.Length > 1)
                    throw new ApplicationException("Darf es nicht geben!?!");
                if (stockEntries.FirstOrDefault() is JsonElement stockEntry){
                    if (stockEntry.ValueKind == JsonValueKind.Object)
                    {
                        var stockId = stockEntry.GetProperty("Stock").GetProperty("Id").GetInt32();
                        var stockUId = stocks.FirstOrDefault(s => s.Key == stockId).Value;
                        var stock = _db.Securities.AsNoTracking().FirstOrDefault(s => s.Id == stockUId && s.OwnerUserId == userId);
                        if (stock is not null)
                        {
                            entry.Description = $"{entry.Description} [Wertpapier: {stock.Name} ({stock.Identifier})]";
                        }
                    }
                }

                var fixedAssetEntries = fixedAssetLedgerEntries.EnumerateArray().Where(e =>
                {
                    var sourceLedger = e.GetProperty("SourceLedgerEntry");
                    if (sourceLedger.ValueKind == JsonValueKind.Null)
                        return false;
                    return sourceLedger.GetProperty("Id").GetInt32() == entry.Id;
                }).ToArray();
                if (fixedAssetEntries.Length > 1)
                    throw new ApplicationException("Darf es nicht geben!?!");
                if (fixedAssetEntries.FirstOrDefault() is JsonElement fixedAssetEntry)
                {
                    var sourceContact = (entry.SourceContact is null) ? null: _db.Contacts.FirstOrDefault(c => c.Id == entry.SourceContact.UID);
                    if (fixedAssetEntry.ValueKind == JsonValueKind.Object && sourceContact is not null && sourceContact.Type == ContactType.Self)
                    {
                        var savingsPlanId = fixedAssetEntry.GetProperty("FixedAsset").GetProperty("Id").GetInt32();
                        var savingsPlanUId = savingsPlans.FirstOrDefault(s => s.Key == savingsPlanId).Value;
                        var savingsPlan = _db.SavingsPlans.AsNoTracking().FirstOrDefault(s => s.Id == savingsPlanUId && s.OwnerUserId == userId);
                        if (savingsPlan is not null)
                        {
                            entry.Description = $"{entry.Description} [Sparplan: {savingsPlan.Name}]";
                        }
                    }
                }
                return entry;
            }),
            BankAccountJournalLines = emptyArray
        };
        var dataJson = JsonSerializer.Serialize(dataObj);

        var fileContent = $"{metaJson}\n{dataJson}";
        var fileBytes = Encoding.UTF8.GetBytes(fileContent);

        // 3) Kontoauszug-Entwürfe erzeugen (Enumeration zwingend, sonst passiert nichts)
        await foreach (var draft in _statementDraftService.CreateDraftAsync(userId, "backup.ndjson", fileBytes, ct))
        {
            MarkDuplicates(draft);
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
            foreach (var entry in group)
            {
                entry.ResetOpen();
            }
        }

        if (duplicateGroups.Count > 0)
        {
            _db.SaveChanges();
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportStocks(JsonElement stocks, Guid userId)
    {
        if (stocks.ValueKind != JsonValueKind.Undefined)
            foreach (var stock in stocks.EnumerateArray())
            {
                var dataId = stock.GetProperty("Id").GetInt32();
                var name = stock.GetProperty("Name").GetString();
                var description = stock.GetProperty("Description").GetString();
                var symbol = stock.GetProperty("Symbol").GetString();
                var avCode = stock.GetProperty("AlphaVantageSymbol").GetString();
                var currencyCode = stock.GetProperty("CurrencyCode").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newStock = new Security(userId, name, symbol, description, avCode, currencyCode ?? "EUR", null);
                var existing = _db.Securities.FirstOrDefault(s => s.OwnerUserId == userId && ((s.Identifier == newStock.Identifier && newStock.Identifier != "") || (s.AlphaVantageCode == newStock.AlphaVantageCode && newStock.AlphaVantageCode != "" && newStock.AlphaVantageCode != "-")));
                if (existing is null)
                    _db.Securities.Add(newStock);
                else
                {
                    existing.Update(name, symbol, description, avCode, currencyCode ?? "EUR", null);
                    newStock = existing;
                    _db.Securities.Update(existing);
                }
                _db.SaveChanges();
                yield return new KeyValuePair<int, Guid>(dataId, newStock.Id);
            }
    }

    private IEnumerable<KeyValuePair<string, Guid>> ImportAccounts(JsonElement bankAccounts, Guid userId)
    {
        var isEmpty = !_db.Accounts.Any(acc => acc.OwnerUserId == userId);
        if (bankAccounts.ValueKind != JsonValueKind.Undefined)
            foreach (var account in bankAccounts.EnumerateArray())
            {
                var iban = account.GetProperty("IBAN").GetString();
                var instituteName = account.GetProperty("InstituteName").ToString();
                var name = account.GetProperty("Description").GetString();
                if (string.IsNullOrWhiteSpace(iban)) continue;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (string.IsNullOrWhiteSpace(instituteName)) continue;

                var contacts = _db.Contacts.Where(c => c.OwnerUserId == userId && c.Name == instituteName).ToArray();
                var contact = contacts.FirstOrDefault();
                if (contact is null)
                {
                    contact = new Contact(userId, instituteName, ContactType.Bank, null);
                    _db.Contacts.Add(contact);
                }
                if (contact.Type != ContactType.Bank)
                {
                    contact.ChangeType(ContactType.Bank);
                    _db.Contacts.Update(contact);
                }

                var contactNames = account.GetProperty("Institute").GetProperty("Names");
                if (contactNames.ValueKind != JsonValueKind.Null)
                    foreach (var contactName in contactNames.EnumerateArray().Select(n => n.GetString()).Where(n => !string.IsNullOrWhiteSpace(n)))
                    {
                        var pattern = contactName ?? instituteName;
                        var existing = _db.AliasNames.Where(a => a.ContactId == contact.Id && a.Pattern == pattern).Any();
                        if (!existing)
                            _db.AliasNames.Add(new AliasName(contact.Id, pattern));
                    }

                var existingAccount = _db.Accounts.FirstOrDefault(a => a.OwnerUserId == userId && a.Iban == iban);
                if (existingAccount is not null)
                {
                    existingAccount.SetBankContact(contact.Id);
                    _db.Accounts.Update(existingAccount);
                    yield return new KeyValuePair<string, Guid>(iban, existingAccount.Id);
                }
                else
                {
                    var newAccount = new Account(userId, isEmpty ? AccountType.Giro : AccountType.Savings, name, iban, contact.Id);
                    _db.Accounts.Add(newAccount);
                    yield return new KeyValuePair<string, Guid>(iban, newAccount.Id);
                }
                _db.SaveChanges();
            }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportSavingsPlans(JsonElement fixedAssets, IEnumerable<KeyValuePair<int, Guid>> savingsPlanCategories, Guid userId)
    {
        if (fixedAssets.ValueKind != JsonValueKind.Undefined)
            foreach (var fixedAsset in fixedAssets.EnumerateArray())
            {
                var dataId = fixedAsset.GetProperty("Id").GetInt32();
                var name = fixedAsset.GetProperty("Name").GetString();
                var expectedPurchaseActive = fixedAsset.GetProperty("ExpectedPurchaseActive").GetBoolean();
                var amount = fixedAsset.GetProperty("ExpectedPurchaseAmount").GetDecimal();
                var dueDate = fixedAsset.GetProperty("ExpectedPurchaseDate").GetDateTime();
                var interval = fixedAsset.GetProperty("PurchaseInterval").GetInt32();
                var status = fixedAsset.GetProperty("Status").GetInt32();
                var contractNo = "";
                if (fixedAsset.TryGetProperty("ContractNo", out var contract))
                    contractNo = contract.ValueKind == JsonValueKind.String ? contract.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                var savingsPlanName = name;
                var nameExists = _db.SavingsPlans.Any(p => p.OwnerUserId == userId && p.Name == savingsPlanName);
                var counter = 1;
                while (nameExists)
                {
                    savingsPlanName = $"{name} ({++counter})";
                    nameExists = _db.SavingsPlans.Any(p => p.OwnerUserId == userId && p.Name == savingsPlanName);
                }
                var newSavingsPlan = new SavingsPlan(
                    userId,
                    savingsPlanName,
                    expectedPurchaseActive ? (interval == 0 ? SavingsPlanType.OneTime : SavingsPlanType.Recurring) : SavingsPlanType.Open,
                    amount,
                    expectedPurchaseActive ? dueDate : null,
                    interval == 0 ? null : (SavingsPlanInterval)(interval - 1));
                newSavingsPlan.SetContractNumber(contractNo);
                //if (status == 3)
                  //  newSavingsPlan.Archive();

                if (fixedAsset.TryGetProperty("Category", out var categoryProp))
                    if (categoryProp.TryGetProperty("Id", out var categoryIdProp) && categoryIdProp.ValueKind == JsonValueKind.Number)
                    {
                        var categoryId = categoryIdProp.GetInt32();
                        var matchingCategory = savingsPlanCategories.FirstOrDefault(c => c.Key == categoryId);
                        if (matchingCategory.Value != Guid.Empty)
                        {
                            newSavingsPlan.SetCategory(matchingCategory.Value);
                        }
                    }
                _db.SavingsPlans.Add(newSavingsPlan);
                _db.SaveChanges();
                yield return new KeyValuePair<int, Guid>(dataId, newSavingsPlan.Id);
            }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportSavingsPlanCategories(JsonElement fixedAssetCategories, Guid userId)
    {
        if (fixedAssetCategories.ValueKind != JsonValueKind.Undefined)
            foreach (var category in fixedAssetCategories.EnumerateArray())
            {
                var dataId = category.GetProperty("Id").GetInt32();
                var name = category.GetProperty("Name").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newCategory = new SavingsPlanCategory(userId, name);
                var existingCategory = _db.SavingsPlanCategories.FirstOrDefault(c => c.Name == newCategory.Name && c.OwnerUserId == userId);
                if (existingCategory is null)
                    _db.SavingsPlanCategories.Add(newCategory);
                else
                    newCategory = existingCategory;
                yield return new KeyValuePair<int, Guid>(dataId, newCategory.Id);
            }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportContacts(JsonElement contacts, IEnumerable<KeyValuePair<int, Guid>> contactCategories, Guid userId)
    {
        if (contacts.ValueKind != JsonValueKind.Undefined)
        {
            var existingContacts = _db.Contacts.ToArray();
            var counter = 0;
            Debug.WriteLine($"Import der Kontakte: {existingContacts.Length} vorher!");
            foreach (var contact in contacts.EnumerateArray())
            {
                var dataId = contact.GetProperty("Id").GetInt32();
                var name = contact.GetProperty("Name").GetString();
                var isPaymentProvider = contact.GetProperty("IsPaymentProvider").GetBoolean();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newContact = new Contact(userId, name, ContactType.Organization, null, null, isPaymentProvider);
                if (contact.TryGetProperty("Category", out var categoryProp))
                    if (categoryProp.TryGetProperty("Id", out var categoryIdProp) && categoryIdProp.ValueKind == JsonValueKind.Number)
                    {
                        var categoryId = categoryIdProp.GetInt32();
                        var matchingCategory = contactCategories.FirstOrDefault(c => c.Key == categoryId);
                        if (matchingCategory.Value != Guid.Empty)
                        {
                            newContact.SetCategory(matchingCategory.Value);
                        }
                    }

                var existing = _db.Contacts.FirstOrDefault(c => c.Name == newContact.Name && c.OwnerUserId == userId);
                if (existing is not null)
                    newContact = existing;
                else
                {
                    counter += 1;
                    _db.Contacts.Add(newContact);
                }

                var contactNames = contact.GetProperty("Names");
                switch(contactNames.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var alias in contactNames.EnumerateArray().Select(n =>
                        {
                            switch(n.ValueKind)
                            {
                                case JsonValueKind.String:
                                    return n.GetString();
                                case JsonValueKind.Object:
                                    return n.GetProperty("Name").GetString();
                                default: return "";
                            }
                        })
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Where(n => !_db.AliasNames.Any(a => a.ContactId == newContact.Id && a.Pattern == n)))
                            _db.AliasNames.Add(new AliasName(newContact.Id, alias));
                        break;
                }
                                    
                yield return new KeyValuePair<int, Guid>(dataId, newContact.Id);
            }
            existingContacts = _db.Contacts.ToArray();
            Debug.WriteLine($"Import der Kontakte: {existingContacts.Length} nachher!");
            Debug.WriteLine($"Import der Kontakte: {counter} eingefügt!");
        }
    }

    private IEnumerable<KeyValuePair<int, Guid>> ImportContactCategories(JsonElement contactCategories, Guid userId)
    {
        if (contactCategories.ValueKind != JsonValueKind.Undefined)
            foreach (var category in contactCategories.EnumerateArray())
            {
                var dataId = category.GetProperty("Id").GetInt32();
                var name = category.GetProperty("Name").GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                var newCategory = new ContactCategory(userId, name);
                var existingCategory = _db.ContactCategories.FirstOrDefault(c => c.Name == newCategory.Name && c.OwnerUserId == userId);
                if (existingCategory is null)
                    _db.ContactCategories.Add(newCategory);
                else
                    newCategory = existingCategory;
                yield return new KeyValuePair<int, Guid>(dataId, newCategory.Id);
            }
    }

    private sealed class BackupMeta
    {
        public string Type { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    private sealed class BackupData
    {
        public JsonElement BankAccounts { get; set; }
        public JsonElement Contacts { get; set; }
        public JsonElement ContactCategories { get; set; }
        public JsonElement FixedAssetCategories { get; set; }
        public JsonElement FixedAssets { get; set; }
        public JsonElement Stocks { get; set; }
        public BackupBankAccountLedgerEntry[]? BankAccountLedgerEntries { get; set; }
        public JsonElement StockLedgerEntries { get; set; }
        public JsonElement FixedAssetLedgerEntries { get; set; }
        // Weitere Properties nach Bedarf
    }
    private sealed class BackupBankAccountLedgerEntry
    {
        public int Id { get; set; }
        public JsonElement Account { get; set; }
        public DateTime PostingDate { get; set; }
        public DateTime ValutaDate { get; set; }
        public decimal Amount { get; set; }
        public string? CurrencyCode { get; set; }
        public string? PostingDescription { get; set; }
        public string? Description { get; set; }
        public string? SourceName { get; set; }
        public BackupContact? SourceContact { get; set; }
        public JsonElement IsCostNeutral { get; set; }

    }
    private sealed class BackupContact
    {
        public int Id { get; set; }
        public Guid? UID { get; set; }
    }
}