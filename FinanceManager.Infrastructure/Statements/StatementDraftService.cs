using FinanceManager.Application.Accounts;
using FinanceManager.Application.Aggregates;
using FinanceManager.Application.Attachments; // added
using FinanceManager.Application.Budget;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Portfolio;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Accounts; // added for Account type
using FinanceManager.Domain.Attachments; // added
using FinanceManager.Domain.Contacts;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure.Statements.Files;
using FinanceManager.Infrastructure.Statements.Parsers;
using FinanceManager.Shared.Extensions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging; // added
using Microsoft.Extensions.Logging.Abstractions; // added
using System.Globalization;
using System.Runtime.CompilerServices;

namespace FinanceManager.Infrastructure.Statements;

/// <summary>
/// Service that handles creation, classification and booking of statement drafts based on parsed statement files.
/// Provides operations to create drafts from uploaded files, manage draft entries and perform booking into postings.
/// </summary>
public sealed partial class StatementDraftService : IStatementDraftService
{
    private readonly AppDbContext _db;
    private readonly IReadOnlyList<IStatementFileParser> _statementFileParsers;
    private readonly IPostingAggregateService _aggregateService;
    private readonly IAccountService _accountService;
    private readonly IStatementFileFactory statementFileFactory;
    private readonly ILogger<StatementDraftService> _logger; // added
    // optional localizer (not required for current implementation)
    // private readonly Microsoft.Extensions.Localization.IStringLocalizer<StatementDraftService> _localizer; // optional localizer
    private readonly IAttachmentService? _attachments; // optional to keep compatibility with tests
    private readonly IReportCacheService? _reportCacheService;
    private readonly IPortfolioAnalysisReportCacheService? _portfolioAnalysisReportCacheService;
    private readonly IBudgetImpactEvaluationService? _budgetImpactEvaluationService;
    private readonly IKnownContactCatalog? _knownContactCatalog;
    // no-op
    private List<StatementDraftDto>? allDrafts = null;
    private List<FinanceManager.Domain.Securities.Security>? allSecurities = null;
    private List<Domain.Accounts.Account>? allAccounts = null;

    // helper: move entry attachments to bank posting and create references for other postings
    private async Task PropagateEntryAttachmentsAsync(
        Guid ownerUserId,
        StatementDraftEntry entry,
        Guid bankPostingId, IEnumerable<Guid> otherPostingIds, CancellationToken ct)
    {
        if (_attachments == null) { return; }
        var list = await _attachments.ListAsync(ownerUserId, AttachmentEntityKind.StatementDraftEntry, entry.Id, 0, 200, categoryId: null, isUrl: null, q: null, ct);
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

    /// <summary>
    /// Metadata describing the last import split operation performed by <see cref="CreateDraftAsync"/>.
    /// </summary>
    /// <returns>The result.</returns>
    public ImportSplitInfo? LastImportSplitInfo { get; private set; } // exposes metadata of last CreateDraftAsync call - scoped service

    /// <summary>
    /// Details about how an import was split into drafts.
    /// </summary>
    /// <param name="ConfiguredMode">Configured import split mode.</param>
    /// <param name="EffectiveMonthly">Whether monthly grouping was used.</param>
    /// <param name="DraftCount">Number of drafts produced.</param>
    /// <param name="TotalMovements">Total number of movements in the parsed file.</param>
    /// <param name="MaxEntriesPerDraft">Configured maximum entries per draft.</param>
    /// <param name="LargestDraftSize">Size of the largest produced draft.</param>
    /// <param name="MonthlyThreshold">Threshold used to decide monthly splitting.</param>
    /// <returns>The result.</returns>
    public sealed record ImportSplitInfo(
        ImportSplitMode ConfiguredMode,
        bool EffectiveMonthly,
        int DraftCount,
        int TotalMovements,
        int MaxEntriesPerDraft,
        int LargestDraftSize,
        int MonthlyThreshold
    );

    /// <summary>
    /// Initializes a new instance of the <see cref="StatementDraftService"/> class.
    /// </summary>
    /// <param name="db">Database context.</param>
    /// <param name="aggregateService">Posting aggregate service used to update aggregates when postings are created.</param>
    /// <param name="accountService">Account service used to resolve account metadata.</param>
    /// <param name="statementFileFactory">Factory used to construct the correct <see cref="IStatementFile"/> for an uploaded file's format.</param>
    /// <param name="readers">Optional collection of statement file readers to parse uploaded files; a default set is used when not provided.</param>
    /// <param name="logger">Optional logger instance; a null logger is used when omitted.</param>
    /// <param name="attachments">Optional attachment service for storing and moving attachments.</param>
    /// <param name="reportCacheService">Optional report cache service invalidated when new postings are created from a draft.</param>
    /// <param name="budgetImpactEvaluationService">Optional service used to evaluate how committed postings affect budget purposes.</param>
    /// <param name="knownContactCatalog">Optional catalog used to suggest known contacts during classification.</param>
    /// <param name="portfolioAnalysisReportCacheService">Optional portfolio analysis report cache invalidated when postings affecting a security are created.</param>
    public StatementDraftService(
        AppDbContext db,
        IPostingAggregateService aggregateService,
        IAccountService accountService,
        IStatementFileFactory statementFileFactory,
        IEnumerable<IStatementFileParser>? readers = null,
        ILogger<StatementDraftService>? logger = null,
        IAttachmentService? attachments = null,
        IReportCacheService? reportCacheService = null,
        IBudgetImpactEvaluationService? budgetImpactEvaluationService = null,
        IKnownContactCatalog? knownContactCatalog = null,
        IPortfolioAnalysisReportCacheService? portfolioAnalysisReportCacheService = null)
    {
        _db = db;
        _aggregateService = aggregateService;
        _accountService = accountService;
        this.statementFileFactory = statementFileFactory;
        _logger = logger ?? NullLogger<StatementDraftService>.Instance;
        _attachments = attachments;
        _reportCacheService = reportCacheService;
        _portfolioAnalysisReportCacheService = portfolioAnalysisReportCacheService;
        _budgetImpactEvaluationService = budgetImpactEvaluationService;
        _knownContactCatalog = knownContactCatalog;
        // localization not required in constructor
        _statementFileParsers = (readers is not null && readers.ToList().Any()) ? readers.ToList() : new List<IStatementFileParser>
        {
            new ING_PDF_StatementFileParser(NullLogger<ING_PDF_StatementFileParser>.Instance),
            new ING_CSV_StatementFileParser(NullLogger<ING_CSV_StatementFileParser>.Instance),
            new Wuestenrot_StatementFileParser(NullLogger<Wuestenrot_StatementFileParser>.Instance),
            new Barclays_PDF_StatementFileParser(NullLogger<Barclays_PDF_StatementFileParser>.Instance),
            new Backup_JSON_StatementFileParser()
        };
    }

    private async Task MarkReportCacheForEntryAsync(StatementDraftEntry entry, CancellationToken ct)
    {
        if (_reportCacheService == null)
        {
            return;
        }

        var bookingDate = DateOnly.FromDateTime(entry.BookingDate);
        var valutaDate = DateOnly.FromDateTime(entry.ValutaDate ?? entry.BookingDate);
        var periodFrom = bookingDate <= valutaDate ? bookingDate : valutaDate;
        var periodTo = bookingDate >= valutaDate ? bookingDate : valutaDate;

        await _reportCacheService.MarkBudgetReportCacheEntriesForUpdateAsync(periodFrom, periodTo, ct);
    }

    private async Task InvalidatePortfolioAnalysisReportCacheAfterBookingAsync(
        Guid ownerUserId,
        Account account,
        bool createdSecurityPostings,
        CancellationToken ct)
    {
        if (_portfolioAnalysisReportCacheService == null)
        {
            return;
        }

        if (!createdSecurityPostings && !account.SecurityProcessingEnabled)
        {
            return;
        }

        await _portfolioAnalysisReportCacheService.InvalidateCacheAsync(ownerUserId, ct);
    }

    /// <summary>
    /// Saves multiple core fields and assignments for a specific draft entry after validating domain constraints.
    /// </summary>
    /// <param name="draftId">Identifier of the draft containing the entry.</param>
    /// <param name="entryId">Identifier of the entry to update.</param>
    /// <param name="ownerUserId">Owner user id performing the operation.</param>
    /// <param name="contactId">Optional contact id to assign or null to clear.</param>
    /// <param name="isCostNeutral">Optional cost-neutral flag to set.</param>
    /// <param name="savingsPlanId">Optional savings plan id to assign or null to clear.</param>
    /// <param name="archiveOnBooking">Optional flag whether to archive the savings plan on booking.</param>
    /// <param name="securityId">Optional security id to assign or null to clear.</param>
    /// <param name="transactionType">Optional security transaction type.</param>
    /// <param name="quantity">Optional security quantity.</param>
    /// <param name="feeAmount">Optional security fee amount.</param>
    /// <param name="taxAmount">Optional security tax amount.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="StatementDraftEntryDto"/>, or null when the draft or entry was not found or the draft is not editable.</returns>
    /// <exception cref="FinanceManager.Application.Exceptions.DomainValidationException">Thrown when domain validation fails for savings or security assignments.</exception>
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
        // Savings plan - only validate when client explicitly provides a savingsPlanId (assignment)
        // If savingsPlanId is null it is considered a clear request and validation for assignment should not block it.
        if (savingsPlanId.HasValue)
        {
            var effContactIdForSavings = contactId ?? entry.ContactId;
            if (!effContactIdForSavings.HasValue)
            {
                throw new FinanceManager.Application.Exceptions.DomainValidationException("SAVINGSPLAN_NO_CONTACT", "Cannot assign a savings plan when no contact is selected.");
            }
            var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == effContactIdForSavings.Value && c.OwnerUserId == ownerUserId, ct);
            if (contact == null || contact.Type != ContactType.Self)
            {
                throw new FinanceManager.Application.Exceptions.DomainValidationException("SAVINGSPLAN_INVALID_CONTACT", "Savings plan can only be assigned when the contact is your own (Self).");
            }
            if (!draft.DetectedAccountId.HasValue)
            {
                throw new FinanceManager.Application.Exceptions.DomainValidationException("SAVINGSPLAN_NO_ACCOUNT", "Cannot assign a savings plan because no account is detected for this draft.");
            }
            var accountForSavings = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId.Value, ct);
            if (accountForSavings == null || accountForSavings.SavingsPlanExpectation == SavingsPlanExpectation.None)
            {
                throw new FinanceManager.Application.Exceptions.DomainValidationException("SAVINGSPLAN_ACCOUNT_NOT_ALLOWED", "The detected account does not allow assigning a savings plan.");
            }
        }
        // Apply incoming assignment/clear after validation
        if (entry.SavingsPlanId != savingsPlanId)
        {
            entry.AssignSavingsPlan(savingsPlanId);
        }
        if (archiveOnBooking.HasValue)
        {
            entry.SetArchiveSavingsPlanOnBooking(archiveOnBooking.Value);
        }

        // Security - only validate when the client provided at least one security-related field (assignment/change)
        var anyIncomingSecurityField = securityId.HasValue || transactionType.HasValue || quantity.HasValue || feeAmount.HasValue || taxAmount.HasValue;
        if (anyIncomingSecurityField)
        {
            // Determine effective resulting values for validation (use existing values for fields not provided)
            var effectiveSecurityId = securityId ?? entry.SecurityId;
            var effectiveTxType = transactionType ?? entry.SecurityTransactionType;
            var effectiveQuantity = quantity ?? entry.SecurityQuantity;
            var effectiveFee = feeAmount ?? entry.SecurityFeeAmount;
            var effectiveTax = taxAmount ?? entry.SecurityTaxAmount;

            var willHaveSecurity = effectiveSecurityId.HasValue || effectiveTxType.HasValue || effectiveQuantity.HasValue || effectiveFee.HasValue || effectiveTax.HasValue;
            if (willHaveSecurity)
            {
                var effContactId = contactId ?? entry.ContactId;
                Guid? bankContactId = null;
                Account? accountForSecurity = null;
                if (draft.DetectedAccountId.HasValue)
                {
                    accountForSecurity = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == draft.DetectedAccountId.Value, ct);
                    bankContactId = accountForSecurity?.BankContactId;
                }
                if (accountForSecurity != null && !accountForSecurity.SecurityProcessingEnabled)
                {
                    throw new FinanceManager.Application.Exceptions.DomainValidationException("SECURITY_ACCOUNT_NOT_ALLOWED", "Security processing is not allowed for the detected account.");
                }
                if (!effContactId.HasValue || bankContactId == null || effContactId.Value != bankContactId.Value)
                {
                    throw new FinanceManager.Application.Exceptions.DomainValidationException("SECURITY_INVALID_CONTACT", "Security-related fields can only be set when the contact equals the bank contact of the detected account.");
                }
            }
        }
        // Apply security changes after validation
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
            entry.EntryNumber,
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

    /// <summary>
    /// Adds statement detail information (fees, taxes, security quantities) to existing drafts when a parsed statement contains such details.
    /// </summary>
    /// <param name="ownerUserId">Owner user id to scope the operation.</param>
    /// <param name="originalFileName">Original file name of the provided details file.</param>
    /// <param name="fileBytes">File content bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AddStatementDetailsAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, CancellationToken ct)
    {
        var statementFile = statementFileFactory.Load(originalFileName, fileBytes);
        if (statementFile is null)
            return;
        var parsedDraft = _statementFileParsers
            .Select(reader => reader.ParseDetails(statementFile))
            .Where(results => results is not null)
            .SelectMany(results => results!)
            .Where(result => result.Movements.Any())
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
        if (line is null)
        {
            // Movements is guaranteed non-empty by the "result.Movements.Any()" filter above, but guard defensively.
            return;
        }
        // line.Subject can legitimately be null (not every parsed statement movement carries a subject text);
        // treat that as "no match" rather than throwing.
        var security = allSecurities.FirstOrDefault(s => s.IsActive && (line.Subject?.Contains(s.Identifier, StringComparison.OrdinalIgnoreCase) ?? false));
        if (security is not null)
        {
            // Respect account setting: if security processing is disabled on the detected account, do not add security details
            if (account == null || !account.SecurityProcessingEnabled)
            {
                // nothing to do - skip applying security details when account disallows it
            }
            else
            {
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

                    if (draftEntries.Count > 1)
                    {
                        var entry2 = draftEntries.FirstOrDefault();
                        if (entry2 is null) continue; // draftEntries.Count > 1 guarantees a first element; guard defensively
                        ApplySecurityDetails(entry2, line);
                        await _db.SaveChangesAsync(ct);

                        foreach (var entry3 in draftEntries.Skip(1))
                        {
                            entry3.MarkAlreadyBooked();
                            await _db.SaveChangesAsync(ct);
                        }
                        continue;
                    }

                    var entry = draftEntries.FirstOrDefault();
                    if (entry is null) continue; // the "if (!draftEntries.Any()) continue;" check above guarantees a first element; guard defensively
                    ApplySecurityDetails(entry, line);
                    await _db.SaveChangesAsync(ct);
                    break;
                }
            }
        }
    }

    private static void ApplySecurityDetails(StatementDraftEntry entry, StatementMovement line)
    {
        entry.SetSecurity(entry.SecurityId, line.PostingDescription switch
        {
            "Dividend" => SecurityTransactionType.Dividend,
            "Sell" => SecurityTransactionType.Sell,
            "Buy" => SecurityTransactionType.Buy,
            _ => entry.SecurityTransactionType
        }, line.PostingDescription switch
        {
            "Dividend" => null,
            "Sell" => line.Quantity ?? entry.SecurityQuantity,
            "Buy" => line.Quantity ?? entry.SecurityQuantity,
            _ => entry.SecurityQuantity
        }, line.FeeAmount ?? entry.SecurityFeeAmount, line.TaxAmount ?? entry.SecurityTaxAmount);
    }

    /// <summary>
    /// Creates statement drafts from the uploaded file contents, splitting the imports into multiple drafts if needed based on user settings.
    /// </summary>
    /// <param name="ownerUserId">Owner user id for the drafts.</param>
    /// <param name="originalFileName">Original file name of the uploaded statement file.</param>
    /// <param name="fileBytes">Byte content of the uploaded file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of created <see cref="StatementDraftDto"/> instances.</returns>
    /// <remarks>
    /// The method parses the file, determines the split configuration (monthly or fixed size), creates draft headers,
    /// and adds movements to the drafts. It also logs the import split details.
    /// </remarks>
    public async IAsyncEnumerable<StatementDraftDto> CreateDraftAsync(Guid ownerUserId, string originalFileName, byte[] fileBytes, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Starting CreateDraftAsync for user {User} and file {File}", ownerUserId, originalFileName);
        var statementFile = statementFileFactory.Load(originalFileName, fileBytes);
        var parsedResults = (statementFile is null) ? null : _statementFileParsers
            .Select(reader => reader.Parse(statementFile))
            .Where(results => results is not null)
            .SelectMany(results => results!)
            .Where(result => result.Movements.Any())
            .ToList();
        if (parsedResults is null || !parsedResults.Any())
        {
            _logger.LogWarning("No valid statement file reader found or no movements detected for user {User} and file {File}", ownerUserId, originalFileName);
            await AddStatementDetailsAsync(ownerUserId, originalFileName, fileBytes, ct);
        }

        if (parsedResults is not null && parsedResults.Any())
        {
            int totalDraftCount = 0;
            int totalMovements = 0;
            int totalLargest = 0;
            ImportSplitMode? aggregatedMode = null;
            bool aggregatedUseMonthly = false;
            int aggregatedMax = 250;
            int aggregatedThreshold = 250;

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

            aggregatedMode = mode;
            aggregatedMax = maxPerDraft;
            aggregatedThreshold = monthlyThreshold;

            foreach (var parsedDraft in parsedResults)
            {
                ct.ThrowIfCancellationRequested();

                var allMovements = parsedDraft.Movements
                    .OrderBy(m => m.BookingDate)
                    .ThenBy(m => m.Subject)
                    .ToList();
                _logger.LogInformation("Parsed {MovementCount} movements from file {File} for user {User}", allMovements.Count, originalFileName, ownerUserId);

                totalMovements += allMovements.Count;

                bool useMonthly = mode switch
                {
                    ImportSplitMode.Monthly => true,
                    ImportSplitMode.FixedSize => false,
                    ImportSplitMode.MonthlyOrFixed => allMovements.Count > monthlyThreshold,
                    _ => false
                };
                aggregatedUseMonthly = aggregatedUseMonthly || useMonthly;

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

                var preliminaryGroups = new List<MovementGroup>();

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
                            preliminaryGroups.Add(new MovementGroup(monthLabel, parts[0], false, g.Key.Year, g.Key.Month));
                        }
                        else
                        {
                            for (int p = 0; p < parts.Count; p++)
                            {
                                preliminaryGroups.Add(new MovementGroup($"{monthLabel} (Teil {p + 1})", parts[p], true, g.Key.Year, g.Key.Month));
                            }
                        }
                    }

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
                        preliminaryGroups.Add(new MovementGroup(string.Empty, chunks[0], false, null, null));
                    }
                    else
                    {
                        for (int i = 0; i < chunks.Count; i++)
                        {
                            preliminaryGroups.Add(new MovementGroup($"(Teil {i + 1})", chunks[i], false, null, null));
                        }
                    }
                }

                totalDraftCount += preliminaryGroups.Count;
                var largest = preliminaryGroups.Count == 0 ? 0 : preliminaryGroups.Max(g => g.Movements.Count);
                if (largest > totalLargest) totalLargest = largest;

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

                    foreach (var movement in group.Movements.OrderBy(m => m.EntryNumber).ThenBy(m => m.BookingDate))
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

            LastImportSplitInfo = new ImportSplitInfo(aggregatedMode ?? mode, aggregatedUseMonthly, totalDraftCount, totalMovements, aggregatedMax, totalLargest, aggregatedThreshold);

            _logger.LogInformation("Completed CreateDraftAsync for user {User} and file {File}", ownerUserId, originalFileName);
        }
    }

    /// <summary>
    /// Represents a group of statement movements that form one draft.
    /// </summary>
    /// <param name="Label">Display label of the group.</param>
    /// <param name="Movements">Movements belonging to the group.</param>
    /// <param name="IsSplitPart">Whether the group is a partial chunk of a month.</param>
    /// <param name="Year">Year of the month group, if monthly-split.</param>
    /// <param name="Month">Month of the month group, if monthly-split.</param>
    /// <returns>The result.</returns>
    private sealed record MovementGroup(string Label, List<StatementMovement> Movements, bool IsSplitPart, int? Year, int? Month);

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
    /// <param name="input">Monthly groups to merge.</param>
    /// <param name="min">Minimum number of movements per resulting group.</param>
    /// <returns>The merged groups.</returns>
    private static List<MovementGroup>
        ApplyMonthlyMinMerge(List<MovementGroup> input, int min)
    {
        // Extract only month groups, keeping order
        var list = input;

        // Quick exit
        if (!list.Any(g => !g.IsSplitPart && g.Movements.Count < min))
        {
            return list;
        }

        // Work on copy list
        var result = new List<MovementGroup>();

        // Helper functions
        static bool IsAnchor(MovementGroup g, int minEntries)
            => !g.IsSplitPart && g.Movements.Count >= minEntries;

        static bool IsSmall(MovementGroup g, int minEntries)
            => !g.IsSplitPart && g.Movements.Count < minEntries;

        // Full sequence of smalls without anchor?
        bool anyAnchor = list.Any(g => IsAnchor(g, min));
        if (!anyAnchor)
        {
            // All are small -> greedy groups >= min
            var buffer = new List<MovementGroup>();
            var acc = new List<StatementMovement>();
            var monthLabels = new List<string>();

            void FlushGroup(bool force)
            {
                if (acc.Count == 0) return;
                if (force || acc.Count >= min)
                {
                    var label = BuildMergedLabel(monthLabels);
                    result.Add(new MovementGroup(label, acc.ToList(), false, null, null));
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

            // Remaining falls too small -> append to previous group
            if (acc.Count > 0)
            {
                if (result.Count == 0)
                {
                    // Single group (too small) -> let it be (hardly relevant technically, but no anchor present)
                    var labelSingle = BuildMergedLabel(monthLabels);
                    result.Add(new MovementGroup(labelSingle, acc, false, null, null));
                }
                else
                {
                    var last = result[^1];
                    last.Movements.AddRange(acc);
                    var appendedLabel = $"{last.Label}+{string.Join('+', monthLabels)}";
                    result[^1] = new MovementGroup(appendedLabel, last.Movements, false, null, null);
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
                var smallRun = new List<MovementGroup>();
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
                            current = new MovementGroup(MergeTwoLabels(current.Label, sm.Label), current.Movements, false, null, null);
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

                // Sonderfall: genau 1 kleiner Monat -> an rechten Anchor hängen (Test-Konvention)
                if (smallRun.Count == 1)
                {
                    result.Add(current); // linker Anchor unverändert
                    // Den einen kleinen Monat NICHT hier hinzufügen, sondern später beim rechten Anchor
                    // -> Wir markiert ihn zum Überspringen und mergen beim rechten Anchor
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
                result.Add(new MovementGroup(leftLabel, leftMovs, false, leftAnchor.Year, leftAnchor.Month));

                // Rechter Anchor wird übersprungen und neu eingefügt
                list[look] = new MovementGroup(rightLabel, rightMovs, false, rightAnchor.Year, rightAnchor.Month);

                index = look; // fahre beim rechten Anchor fort (der jetzt evtl. noch weitere Runs hat)
                continue;
            }
            else
            {
                // current ist kleiner Monat (kein Anchor) – kann nur Leading-Run sein (vor erstem Anchor) oder einzelner kleiner vor Anchor (aus Sonderfall oben)
                var leadingRun = new List<MovementGroup>();
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

                foreach (var b in blocks)
                {
                    var label = BuildMergedLabel(b.labels);
                    result.Add(new MovementGroup(label, b.movs, false, null, null));
                }

                if (accMovs.Count > 0)
                {
                    // an Anchor anhängen -> wir modifizieren den Anchor jetzt
                    var anchor = list[l];
                    anchor.Movements.InsertRange(0, accMovs); // vorne einfügen (zeitlich frühere Monate)
                    anchor = new MovementGroup(MergeTwoLabels(BuildMergedLabel(accLabels), anchor.Label), anchor.Movements, false, anchor.Year, anchor.Month);
                    list[l] = anchor;
                }

                index = l; // Anchor selbst wird in nachfolgender Iteration verarbeitet
            }
        }

        return result;

        // Hilfen -----------------------------------------------------

        static (bool attachToPrevious, List<MovementGroup>? groups)
            BuildStandaloneOrAttach(List<MovementGroup> run, int minEntries)
        {
            var groups = new List<MovementGroup>();
            var accMovs = new List<StatementMovement>();
            var accLabels = new List<string>();

            foreach (var g in run)
            {
                accMovs.AddRange(g.Movements);
                accLabels.Add(g.Label.Split(' ')[0]);
                if (accMovs.Count >= minEntries)
                {
                    groups.Add(new MovementGroup(BuildMergedLabel(accLabels), new List<StatementMovement>(accMovs), false, null, null));
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
                last = new MovementGroup(MergeTwoLabels(last.Label, BuildMergedLabel(accLabels)), last.Movements, false, null, null);
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
            else
            {
                // Auto-assignment: look up the IBAN in AccountLinkedIbans and verify account ownership
                var linkedIbanEntry = await (
                    from li in _db.AccountLinkedIbans
                    join a in _db.Accounts on li.AccountId equals a.Id
                    where li.Iban == parsedDraft.Header.IBAN && a.OwnerUserId == ownerUserId
                    select li
                ).FirstOrDefaultAsync(ct);

                if (linkedIbanEntry != null)
                {
                    draft.SetDetectedAccount(linkedIbanEntry.AccountId);
                }
            }
        }

        return draft;
    }

    /// <summary>
    /// New: create an empty draft (no parsed file) and return DTO.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="originalFileName">Original file name for the draft.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="StatementDraftDto"/>.</returns>
    public async Task<StatementDraftDto?> CreateEmptyDraftAsync(Guid ownerUserId, string originalFileName, CancellationToken ct)
    {
        // Create draft with no account number / description
        var draft = new StatementDraft(ownerUserId, originalFileName, accountNumber: null, description: null);
        _db.StatementDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);

        // Classify (will run internal classification logic) and persist any changes
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);

        return Map(draft);
    }

    private async Task<StatementDraftDto> FinishDraftAsync(StatementDraft draft, Guid ownerUserId, CancellationToken ct)
    {
        _db.StatementDrafts.Add(draft);
        await _db.SaveChangesAsync(ct);
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return Map(draft);
    }

    /// <summary>
    /// Returns a list of open drafts for the specified owner, with derived metadata resolved in bulk for efficiency.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="StatementDraftDto"/> instances.</returns>
    /// <remarks>
    /// The method retrieves open drafts for the owner, including extra data like detected account id, bank contact id and attachment symbol id.
    /// </remarks>
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

        // collect detected account ids to resolve bank contact and symbol attachments in batch
        var accountIds = drafts.Where(d => d.DetectedAccountId.HasValue).Select(d => d.DetectedAccountId!.Value).Distinct().ToList();
        var accounts = accountIds.Count > 0
            ? await _db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToListAsync(ct)
            : new List<Domain.Accounts.Account>();

        var contactIds = accounts.Where(a => a.BankContactId != Guid.Empty).Select(a => a.BankContactId).Distinct().ToList();
        var contacts = contactIds.Count > 0
            ? await _db.Contacts.AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToListAsync(ct)
            : new List<Domain.Contacts.Contact>();

        var categoryIds = contacts.Where(c => c.CategoryId.HasValue).Select(c => c.CategoryId!.Value).Distinct().ToList();
        var categories = categoryIds.Count > 0
            ? await _db.ContactCategories.AsNoTracking().Where(cat => categoryIds.Contains(cat.Id)).ToListAsync(ct)
            : new List<Domain.Contacts.ContactCategory>();

        var result = new List<StatementDraftDto>(drafts.Count);
        foreach (var draft in drafts)
        {
            dynamic? refInfo = null;
            bySplitId.TryGetValue(draft.Id, out refInfo);
            Guid? parentDraftId = refInfo?.DraftId;
            Guid? parentEntryId = refInfo?.Id;
            decimal? parentEntryAmount = refInfo?.Amount;

            Guid? detectedAccountBankContactId = null;
            Guid? attachmentSymbolId = null;
            if (draft.DetectedAccountId.HasValue)
            {
                var acct = accounts.FirstOrDefault(a => a.Id == draft.DetectedAccountId.Value);
                if (acct != null)
                {
                    detectedAccountBankContactId = acct.BankContactId;
                    // resolve symbol: account -> contact -> category, treat Guid.Empty as null
                    if (acct.SymbolAttachmentId.HasValue && acct.SymbolAttachmentId.Value != Guid.Empty)
                    {
                        attachmentSymbolId = acct.SymbolAttachmentId;
                    }
                    else
                    {
                        var bc = contacts.FirstOrDefault(c => c.Id == acct.BankContactId);
                        if (bc != null && bc.SymbolAttachmentId.HasValue && bc.SymbolAttachmentId.Value != Guid.Empty)
                        {
                            attachmentSymbolId = bc.SymbolAttachmentId;
                        }
                        else if (bc != null && bc.CategoryId.HasValue)
                        {
                            var cat = categories.FirstOrDefault(x => x.Id == bc.CategoryId.Value);
                            if (cat != null && cat.SymbolAttachmentId.HasValue && cat.SymbolAttachmentId.Value != Guid.Empty)
                            {
                                attachmentSymbolId = cat.SymbolAttachmentId;
                            }
                        }
                    }
                }
            }

            var total = draft.Entries.Sum(e => e.Amount);
            var dto = new StatementDraftDto(
                draft.Id,
                draft.OriginalFileName,
                draft.Description,
                draft.DetectedAccountId,
                detectedAccountBankContactId,
                attachmentSymbolId,
                draft.Status,
                total,
                parentDraftId != null,
                parentDraftId,
                parentEntryId,
                parentEntryAmount,
                draft.UploadGroupId,
                draft.Entries.Select(e => new StatementDraftEntryDto(
                    e.Id,
                    e.EntryNumber,
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
                    e.ArchiveSavingsPlanOnBooking,
                    e.SplitDraftId,
                    e.SecurityId,
                    e.SecurityTransactionType,
                    e.SecurityQuantity,
                    e.SecurityFeeAmount,
                    e.SecurityTaxAmount)).ToList());

            result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Returns a paged list of open drafts for the specified owner. The returned draft DTOs include resolved account symbol info when available.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="skip">Number of items to skip (pagination).</param>
    /// <param name="take">Number of items to take (clamped between 1 and 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="StatementDraftDto"/> instances.</returns>
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

        return drafts.Select(d =>
        {
            var account = d.DetectedAccountId.HasValue ? _accountService.Get(d.DetectedAccountId.Value, d.OwnerUserId) : null;
            return Map(d, bySplitId, account);
        }).ToList();
    }

    /// <summary>
    /// Returns the count of open drafts for the user.
    /// </summary>
    /// <param name="userId">Owner user id.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>Number of open drafts.</returns>
    public async Task<int> GetOpenDraftsCountAsync(Guid userId, CancellationToken token)
    {
        return await _db.StatementDrafts
             .Include(d => d.Entries)
             .Where(d => d.OwnerUserId == userId && d.Status == StatementDraftStatus.Draft)
             .AsNoTracking()
             .CountAsync();
    }

    /// <summary>
    /// Retrieves a draft with entries for the given draft id and owner.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Mapped <see cref="StatementDraftDto"/> or null when not found.</returns>
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

    /// <summary>
    /// Retrieves only draft header information (no entries) for the specified draft id and owner.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Finds the header of the draft that contains the specified entry id, scoped by owner.
    /// </summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<StatementDraftDto?> FindDraftHeaderAsync(Guid entryId, Guid ownerUserId, CancellationToken ct)
    {
        var draftIds = await _db.StatementDraftEntries.Where(entry => entry.Id == entryId).Select(entry => entry.DraftId).ToListAsync();
        var draft = await _db.StatementDrafts
            .FirstOrDefaultAsync(d => draftIds.Contains(d.Id) && d.OwnerUserId == ownerUserId, ct);
        return await GetDraftHeaderAsync(draft?.Id ?? Guid.Empty, ownerUserId, ct);
    }

    /// <summary>
    /// Returns all entries for a draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<IEnumerable<StatementDraftEntryDto>> GetDraftEntriesAsync(Guid draftId, CancellationToken ct)
    {
        var entries = await _db.StatementDraftEntries.Where(e => e.DraftId == draftId).ToListAsync(ct);
        return entries.Select(e => Map(e));
    }

    /// <summary>
    /// Returns a single draft entry DTO or null when not found.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<StatementDraftEntryDto?> GetDraftEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var draftEntry = await _db.StatementDraftEntries.FirstOrDefaultAsync(e => e.DraftId == draftId && e.Id == entryId, ct);
        if (draftEntry is null)
            return null;
        return Map(draftEntry);
    }

    /// <summary>
    /// Adds a new entry to a draft and runs classification for the draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="bookingDate">The booking date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Commits a draft by creating a StatementImport and StatementEntry records for all draft entries and moves attachments.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="accountId">The account id.</param>
    /// <param name="format">The format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="CommitResult"/> on success, or null when draft not found or invalid.</returns>
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

    /// <summary>
    /// Cancels (deletes) a draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<bool> CancelAsync(Guid draftId, Guid ownerUserId, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null) { return false; }
        _db.StatementDrafts.Remove(draft);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Classifies drafts (or a single draft) to populate derived metadata used for matching and presentation.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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
            var resultDto = await GetDraftAsync(draft.Id, ownerUserId, ct);
            // GetDraftAsync can legitimately return null if the draft was concurrently deleted/committed
            // between the initial load above and this re-fetch; skip it rather than adding a null entry.
            if (resultDto is not null)
            {
                resultLust.Add(resultDto);
            }
        }
        return resultLust.FirstOrDefault();
    }

    /// <summary>
    /// Sets the detected account for a draft and re-classifies the draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="accountId">The account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Sets the contact for a draft entry (or clears it) and persists changes.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="contactId">The contact id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Sets or clears the cost-neutral flag for an entry.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="isCostNeutral">The is cost neutral.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Assigns a savings plan to an entry.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="savingsPlanId">The savings plan id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Sets or clears the split-draft association for an entry. Validates constraints for split drafts.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="splitDraftId">The split draft id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Updates core fields of an entry.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="bookingDate">The booking date.</param>
    /// <param name="valutaDate">The valuta date.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="subject">The subject.</param>
    /// <param name="recipientName">The recipient name.</param>
    /// <param name="currencyCode">The currency code.</param>
    /// <param name="bookingDescription">The booking description.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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
            entry.EntryNumber,
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

    /// <summary>
    /// Sets whether the savings plan assigned to an entry should be archived on booking.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="archive">The archive.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Sets security-related fields for a draft entry and returns the modified draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="securityId">The security id.</param>
    /// <param name="transactionType">The transaction type.</param>
    /// <param name="quantity">The quantity.</param>
    /// <param name="feeAmount">The fee amount.</param>
    /// <param name="taxAmount">The tax amount.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Validates a draft and its entries and returns validation messages and overall validity.
    /// </summary>
    /// <param name="draftId">Draft identifier.</param>
    /// <param name="entryId">Optional entry identifier to validate a specific entry.</param>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DraftValidationResultDto"/> containing the validation messages and status.</returns>
    /// <remarks>
    /// The method checks for various validation rules like account assignment, cycle detection in splits,
    /// and rule-based validation for savings plans and securities. It returns all messages so the UI can present them.
    /// </remarks>
    public async Task<DraftValidationResultDto> ValidateAsync(Guid draftId, Guid? entryId, Guid ownerUserId, CancellationToken ct)
    {
        return await ValidateCoreAsync(draftId, entryId, ownerUserId, includeBudgetImpact: true, ct);
    }

    private async Task<DraftValidationResultDto> ValidateCoreAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool includeBudgetImpact, CancellationToken ct)
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

        void Add(string code, string sev, string msg, Guid? draftId, Guid? eId, string prefix = "") => messages.Add(new DraftValidationMessageDto(code, sev, prefix + msg, draftId ?? draft.Id, eId));

        if (draft.DetectedAccountId == null)
        {
            Add("NO_ACCOUNT", "Error", "Validation_NO_ACCOUNT", null, null);
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
            Add("SPLIT_CYCLE_DETECTED", "Error", "Validation_SPLIT_CYCLE_DETECTED", null, null);
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
                Add("SPLIT_DRAFT_HAS_ACCOUNT", "Error", "Validation_SPLIT_DRAFT_HAS_ACCOUNT", draft.Id, parentEntry.Id, prefix);
            }
            var sum = groupEntries.Sum(e => e.Amount);
            if (sum != parentEntry.Amount)
            {
                Add("SPLIT_AMOUNT_MISMATCH", "Error", "Validation_SPLIT_AMOUNT_MISMATCH", draft.Id, parentEntry.Id, prefix);
            }
            foreach (var ce in groupEntries)
            {
                if (ce.ContactId == null)
                {
                    Add("ENTRY_NO_CONTACT", "Error", "Validation_ENTRY_NO_CONTACT", parentEntry.DraftId, parentEntry.Id, prefix);
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, token);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (ce.SplitDraftId == null)
                    {
                        Add("INTERMEDIARY_NO_SPLIT", "Error", "Validation_INTERMEDIARY_NO_SPLIT", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    else
                    {
                        await ValidateSplitBranchAsync(ce, prefix, token);
                    }
                }
                if (self != null && ce.ContactId == self.Id && ce.SavingsPlanId == null)
                {
                    // Determine severity based on parent account setting; default to Warning when account unknown
                    if (account == null)
                    {
                        Add("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", "Validation_SAVINGSPLAN_MISSING_FOR_SELF", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    else
                    {
                        var sev = account.SavingsPlanExpectation switch
                        {
                            SavingsPlanExpectation.Required => "Error",
                            SavingsPlanExpectation.Optional => "Warning",
                            SavingsPlanExpectation.None => null,
                            _ => "Warning"
                        };
                        if (sev != null)
                        {
                            Add("SAVINGSPLAN_MISSING_FOR_SELF", sev, "Validation_SAVINGSPLAN_MISSING_FOR_SELF", parentEntry.DraftId, parentEntry.Id, prefix);
                        }
                    }
                }
                var ceHasSecurity = ce.SecurityId != null
                    || ce.SecurityTransactionType != null
                    || ce.SecurityQuantity != null
                    || ce.SecurityFeeAmount != null
                    || ce.SecurityTaxAmount != null;
                if (ceHasSecurity && account != null)
                {
                    if (!account.SecurityProcessingEnabled)
                    {
                        Add("SECURITY_ACCOUNT_NOT_ALLOWED", "Error", "Validation_SECURITY_ACCOUNT_NOT_ALLOWED", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    if (ce.ContactId != account.BankContactId)
                    {
                        Add("SECURITY_INVALID_CONTACT", "Error", "Validation_SECURITY_INVALID_CONTACT", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    if (ce.SecurityTransactionType == null)
                    {
                        Add("SECURITY_MISSING_TXTYPE", "Error", "Validation_SECURITY_MISSING_TXTYPE", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    if (ce.SecurityTransactionType != null && ce.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (ce.SecurityQuantity == null || ce.SecurityQuantity <= 0m)
                        {
                            Add("SECURITY_MISSING_QUANTITY", "Error", "Validation_SECURITY_MISSING_QUANTITY", parentEntry.DraftId, parentEntry.Id, prefix);
                        }
                    }
                    if (ce.SecurityTransactionType == SecurityTransactionType.Dividend && ce.SecurityQuantity != null)
                    {
                        Add("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", "Validation_SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", parentEntry.DraftId, parentEntry.Id, prefix);
                    }
                    var fee = ce.SecurityFeeAmount ?? 0m;
                    var tax = ce.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(ce.Amount))
                    {
                        Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Validation_SECURITY_FEE_TAX_EXCEEDS_AMOUNT", parentEntry.DraftId, parentEntry.Id, prefix);
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
                    Add("ENTRY_NO_CONTACT", "Error", "Validation_ENTRY_NO_CONTACT", null, e.Id);
                    continue;
                }
                if (e.Status == StatementDraftEntryStatus.Open)
                {
                    Add("ENTRY_NEEDS_CHECK", "Error", "Validation_ENTRY_NEEDS_CHECK", null, e.Id);
                    continue;
                }
                var c = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == e.ContactId && x.OwnerUserId == ownerUserId, ct);
                if (c == null) { continue; }
                if (c.IsPaymentIntermediary)
                {
                    if (e.SplitDraftId == null)
                    {
                        Add("INTERMEDIARY_NO_SPLIT", "Error", "Validation_INTERMEDIARY_NO_SPLIT", null, e.Id);
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
                        // Use account setting to decide severity; default to Warning
                        if (account == null)
                        {
                            Add("SAVINGSPLAN_MISSING_FOR_SELF", "Warning", "Validation_SAVINGSPLAN_MISSING_FOR_SELF", null, e.Id);
                        }
                        else
                        {
                            var sev = account.SavingsPlanExpectation switch
                            {
                                SavingsPlanExpectation.Required => "Error",
                                SavingsPlanExpectation.Optional => "Warning",
                                SavingsPlanExpectation.None => null,
                                _ => "Warning"
                            };
                            if (sev != null)
                            {
                                Add("SAVINGSPLAN_MISSING_FOR_SELF", sev, "Validation_SAVINGSPLAN_MISSING_FOR_SELF", null, e.Id);
                            }
                        }
                    }
                    else if (account != null && account.Type == AccountType.Savings)
                    {
                        Add("SAVINGSPLAN_INVALID_ACCOUNT", "Error", "Validation_SAVINGSPLAN_INVALID_ACCOUNT", null, e.Id);
                    }
                }
                var hasSecurity = e.SecurityId != null
                    || e.SecurityTransactionType != null
                    || e.SecurityQuantity != null
                    || e.SecurityFeeAmount != null
                    || e.SecurityTaxAmount != null;
                if (hasSecurity && account != null)
                {
                    if (!account.SecurityProcessingEnabled)
                    {
                        Add("SECURITY_ACCOUNT_NOT_ALLOWED", "Error", "Validation_SECURITY_ACCOUNT_NOT_ALLOWED", null, e.Id);
                    }
                    if (e.ContactId != account.BankContactId)
                    {
                        Add("SECURITY_INVALID_CONTACT", "Error", "Validation_SECURITY_INVALID_CONTACT", null, e.Id);
                    }
                    if (e.SecurityTransactionType == null)
                    {
                        Add("SECURITY_MISSING_TXTYPE", "Error", "Validation_SECURITY_MISSING_TXTYPE", null, e.Id);
                    }
                    if (e.SecurityTransactionType != null && e.SecurityTransactionType != SecurityTransactionType.Dividend)
                    {
                        if (e.SecurityQuantity == null || e.SecurityQuantity <= 0m)
                        {
                            Add("SECURITY_MISSING_QUANTITY", "Error", "Validation_SECURITY_MISSING_QUANTITY", null, e.Id);
                        }
                    }
                    if (e.SecurityTransactionType == SecurityTransactionType.Dividend && e.SecurityQuantity != null)
                    {
                        Add("SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", "Error", "Validation_SECURITY_QUANTITY_NOT_ALLOWED_FOR_DIVIDEND", null, e.Id);
                    }
                    var fee = e.SecurityFeeAmount ?? 0m;
                    var tax = e.SecurityTaxAmount ?? 0m;
                    if (fee + tax > Math.Abs(e.Amount))
                    {
                        Add("SECURITY_FEE_TAX_EXCEEDS_AMOUNT", "Error", "Validation_SECURITY_FEE_TAX_EXCEEDS_AMOUNT", null, e.Id);
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
                    messages.Add(new("SAVINGSPLAN_GOAL_REACHED_INFO", "Information", $"Validation_SAVINGSPLAN_GOAL_REACHED_INFO|{plan.Name}", draft.Id, null, "savings-plans", plan.Id));
                }
                else if (remaining > 0m && planned > remaining)
                {
                    messages.Add(new("SAVINGSPLAN_GOAL_EXCEEDS", "Warning", $"Validation_SAVINGSPLAN_GOAL_EXCEEDS|{plan.Name}", draft.Id, null, "savings-plans", plan.Id));
                }
                if (wantsArchive && current + planned != target)
                {
                    messages.Add(new("SAVINGSPLAN_ARCHIVE_MISMATCH", "Error", $"Validation_SAVINGSPLAN_ARCHIVE_MISMATCH|{plan.Name}", draft.Id, null, "savings-plans", plan.Id));
                }
            }
        }
        if (entries.Length > 0 && entryId is null && account is not null && account.SavingsPlanExpectation != SavingsPlanExpectation.None)
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
                messages.Add(new("SAVINGSPLAN_DUE", "Information", $"Validation_SAVINGSPLAN_DUE|{plan.Name}|{effectiveDue:d}", draft.Id, null, "savings-plans", plan.Id));
            }
        }
        if (!draft.IsPreliminary && draft.DetectedAccountId.HasValue)
        {
            var hasPreliminary = await _db.Postings.AsNoTracking().AnyAsync(p => p.AccountId == draft.DetectedAccountId.Value && p.Kind == PostingKind.Bank && p.IsPreliminary && !p.ReversedByPostingId.HasValue, ct);
            if (hasPreliminary)
            {
                messages.Add(new("PRELIMINARY_POSTINGS_WILL_BE_REVERSED", "Warning", $"Validation_PRELIMINARY_POSTINGS_WILL_BE_REVERSED|/list/postings/account/{draft.DetectedAccountId.Value}", draft.Id, null, "accounts", draft.DetectedAccountId.Value));
            }
        }
        var isValid = messages.All(m => !string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase));
        var budgetImpact = includeBudgetImpact && _budgetImpactEvaluationService != null
            ? await _budgetImpactEvaluationService.EvaluateDraftImpactAsync(draft.Id, entryId, ownerUserId, ct)
            : null;
        return new DraftValidationResultDto(draft.Id, isValid, messages)
        {
            BudgetImpact = budgetImpact
        };
    }

    /// <summary>
    /// Books a draft (or a single entry) into postings after validation; may perform partial booking.
    /// </summary>
    /// <param name="draftId">The identifier of the draft to book.</param>
    /// <param name="entryId">Optional identifier of a specific entry to book; if not provided, the entire draft is booked.</param>
    /// <param name="ownerUserId">The owner user identifier, used for scoping and validation.</param>
    /// <param name="forceWarnings">Flag to force booking even if there are warnings.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A <see cref="BookingResult"/> indicating success or failure, and providing next steps if partial booking occurred.</returns>
    public async Task<BookingResult> BookAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        _logger.LogInformation("Starting statement booking for draft {DraftId}, entry {EntryId}, owner {OwnerUserId}, forceWarnings {ForceWarnings}", draftId, entryId, ownerUserId, forceWarnings);

        var lockToken = await TryAcquireBookingGuardAsync(ownerUserId, draftId, ct);
        if (!lockToken.HasValue)
        {
            return CreateBookingFailureResult(
                draftId,
                entryId,
                "BOOKING_IN_PROGRESS",
                "A booking operation is already running for this draft.",
                retryable: true);
        }

        try
        {
            if (string.Equals(_db.Database.ProviderName, "Microsoft.EntityFrameworkCore.InMemory", StringComparison.Ordinal))
            {
                return await BookCoreAsync(draftId, entryId, ownerUserId, forceWarnings, ct);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var result = await BookCoreAsync(draftId, entryId, ownerUserId, forceWarnings, ct);
                await transaction.CommitAsync(ct);
                return result;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Statement booking failed and was rolled back for draft {DraftId}, entry {EntryId}", draftId, entryId);
                throw;
            }
        }
        finally
        {
            await ReleaseBookingGuardAsync(ownerUserId, draftId, lockToken.Value, ct);
        }
    }

    private static BookingResult CreateBookingFailureResult(Guid draftId, Guid? entryId, string errorCode, string message, bool retryable)
    {
        var validation = new DraftValidationResultDto(
            draftId,
            false,
            [
                new DraftValidationMessageDto(errorCode, "Error", message, draftId, entryId)
            ]);

        return new BookingResult(false, false, validation, null, null, null)
        {
            ErrorCode = errorCode,
            Retryable = retryable
        };
    }

    private async Task<Guid?> TryAcquireBookingGuardAsync(Guid ownerUserId, Guid draftId, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var lockToken = Guid.NewGuid();
        var expiresUtc = nowUtc.AddMinutes(2);

        var guard = new StatementDraftBookingGuard(ownerUserId, draftId, lockToken, nowUtc, expiresUtc);
        _db.StatementDraftBookingGuards.Add(guard);

        try
        {
            await _db.SaveChangesAsync(ct);
            return lockToken;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _db.Entry(guard).State = EntityState.Detached;
        }

        var existingGuard = await _db.StatementDraftBookingGuards
            .FirstOrDefaultAsync(g => g.OwnerUserId == ownerUserId && g.DraftId == draftId, ct);
        if (existingGuard == null || existingGuard.ExpiresUtc > nowUtc)
        {
            return null;
        }

        existingGuard.LockToken = lockToken;
        existingGuard.AcquiredUtc = nowUtc;
        existingGuard.ExpiresUtc = expiresUtc;
        await _db.SaveChangesAsync(ct);
        return lockToken;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        return ex.InnerException is SqliteException sqliteException && sqliteException.SqliteErrorCode == 19;
    }

    private async Task ReleaseBookingGuardAsync(Guid ownerUserId, Guid draftId, Guid lockToken, CancellationToken ct)
    {
        var guards = await _db.StatementDraftBookingGuards
            .Where(g => g.OwnerUserId == ownerUserId && g.DraftId == draftId && g.LockToken == lockToken)
            .ToListAsync(ct);
        if (guards.Count == 0)
        {
            return;
        }

        _db.StatementDraftBookingGuards.RemoveRange(guards);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<BookingResult> BookCoreAsync(Guid draftId, Guid? entryId, Guid ownerUserId, bool forceWarnings, CancellationToken ct)
    {
        var validation = await ValidateCoreAsync(draftId, entryId, ownerUserId, includeBudgetImpact: false, ct);
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
        if (draft.Status == StatementDraftStatus.Committed)
        {
            return CreateBookingFailureResult(
                draftId,
                entryId,
                "BOOKING_ALREADY_PROCESSED",
                "The statement draft has already been booked.",
                retryable: false);
        }

        // Disallow booking a child draft directly
        bool isChild = await _db.StatementDraftEntries.AsNoTracking().AnyAsync(e => e.SplitDraftId == draft.Id, ct);
        if (isChild)
        {
            return new BookingResult(false, hasWarnings, validation, null, null, null);
        }

        var account = await _db.Accounts.FirstAsync(a => a.Id == draft.DetectedAccountId, ct);
        var self = await _db.Contacts.FirstAsync(c => c.OwnerUserId == ownerUserId && c.Type == ContactType.Self, ct);
        if (!draft.IsPreliminary)
        {
            await ReversePreliminaryPostingsAsync(account.Id, ownerUserId, ct);
        }
        var allEntriesScope = await _db.StatementDraftEntries.Where(e => e.DraftId == draft.Id && (entryId == null || e.Id == entryId)).ToListAsync(ct);
        if (entryId.HasValue && allEntriesScope.Count == 0)
        {
            var entryAlreadyBooked = await _db.Postings.AsNoTracking().AnyAsync(p => p.SourceId == entryId.Value, ct);
            if (entryAlreadyBooked)
            {
                return CreateBookingFailureResult(
                    draftId,
                    entryId,
                    "BOOKING_ALREADY_PROCESSED",
                    "The statement draft entry has already been booked.",
                    retryable: false);
            }
        }

        // ignore entries already booked
        var toBook = (entryId == null ? allEntriesScope : allEntriesScope.Where(e => e.Id == entryId.Value))
            .Where(e => e.Status != StatementDraftEntryStatus.AlreadyBooked && e.Status != StatementDraftEntryStatus.Announced)
            .ToList();

        var budgetImpactSummary = _budgetImpactEvaluationService == null
            ? null
            : await _budgetImpactEvaluationService.EvaluateDraftImpactAsync(draftId, entryId, ownerUserId, ct);

        var updatedPlanIds = new HashSet<Guid>();
        var createdPortfolioSecurityPostings = false;
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
            var bankPosting = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
            _db.Postings.Add(bankPosting); await UpsertAggregatesAsync(bankPosting, token);
            var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
            _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, token);

            if (amount == 0m)
            {
                bankPosting.SetOriginalAmount(e.Amount);
                contactPosting.SetOriginalAmount(e.Amount);
            }

            // Kontosaldo anpassen (nur Bank-Buchung relevant)
            if (amount != 0m)
            {
                account.AdjustBalance(amount);
            }
            await MarkReportCacheForEntryAsync(e, ct);

            // After saving, bankPosting.Id and contactPosting.Id will be set; return both ids
            return (gid, bankPosting.Id, contactPosting.Id);
        }

        // update calls accordingly

        async Task CreateSecurityPostingsAsync(StatementDraftEntry e, Guid gid, CancellationToken token)
        {
            if (e.SecurityId == null) { return; }
            createdPortfolioSecurityPostings = true;
            var fee = Math.Abs(e.SecurityFeeAmount ?? 0m); var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
            if (e.Amount < 0) { fee = -fee; tax = -tax; }
            var factor = e.SecurityTransactionType switch { SecurityTransactionType.Buy => 1, SecurityTransactionType.Sell => -1, SecurityTransactionType.Dividend => -1, _ => -1 };
            var tradeAmount = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.Amount - fee - tax, SecurityTransactionType.Sell => e.Amount + fee + tax, SecurityTransactionType.Dividend => e.Amount + fee + tax, _ => e.Amount + fee + tax };
            var tradeSub = e.SecurityTransactionType switch { SecurityTransactionType.Buy => SecurityPostingSubType.Buy, SecurityTransactionType.Sell => SecurityPostingSubType.Sell, SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend, _ => SecurityPostingSubType.Buy };
            decimal? qty = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.SecurityQuantity.HasValue ? Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Sell => e.SecurityQuantity.HasValue ? -Math.Abs(e.SecurityQuantity.Value) : (decimal?)null, SecurityTransactionType.Dividend => null, _ => e.SecurityQuantity };
            var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, tradeAmount, e.Subject, e.RecipientName, e.BookingDescription, tradeSub, qty).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
            _db.Postings.Add(main); await UpsertAggregatesAsync(main, token);
            if (fee != 0m)
            {
                var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, token);
            }
            if (tax != 0m)
            {
                var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, e.BookingDate, e.ValutaDate ?? e.BookingDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
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

                await MarkReportCacheForEntryAsync(e, ct);

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
                var overrideBookingDate = e.BookingDate;
                await BookSplitDraftGroupAsync(e.SplitDraftId.Value, ownerUserId, draft.Id, self, account, ct, new HashSet<Guid>(), overrideBookingDate, parentBankId, parentContactId);
            }
            else
            {
                // normal booking
                var (gid, bankId, contactId) = await CreateBankAndContactPostingAsync(e, e.Amount, e.ContactId, ct);
                // optional savings plan posting
                Guid? spPostingId = null;
                if (e.SavingsPlanId != null && e.ContactId == self.Id)
                {
                    var spPosting = new Domain.Postings.Posting(e.Id, PostingKind.SavingsPlan, null, null, e.SavingsPlanId, null, e.BookingDate, e.ValutaDate ?? e.BookingDate, -e.Amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                    _db.Postings.Add(spPosting); await UpsertAggregatesAsync(spPosting, ct);
                    spPostingId = spPosting.Id;

                    // Advance recurring savings plan target date if due (month-end rule), using booking date
                    await UpdateRecurringPlanIfDueAsync(e.SavingsPlanId.Value, e.BookingDate, ct);
                }
                await CreateSecurityPostingsAsync(e, gid, ct);

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

                // Attempt to link contact postings for self-transfers
                try
                {
                    var newContactPost = _db.Postings.Local.FirstOrDefault(p => p.SourceId == e.Id && p.Kind == PostingKind.Contact && p.GroupId == gid);
                    if (newContactPost != null && newContactPost.LinkedPostingId == null && newContactPost.ContactId == self.Id)
                    {
                        // Find candidates in DB, then pick the one with the closest booking date to the new posting
                        var candidates = await _db.Postings
                            .Where(p => p.Kind == PostingKind.Contact
                                        && p.ContactId == newContactPost.ContactId
                                        && p.Amount == -newContactPost.Amount // Der Betrag muss identisch sein, nur als Gegenbuchung.
                                        && p.LinkedPostingId == null
                                        && p.SourceId != newContactPost.SourceId
                                        && p.Subject == newContactPost.Subject)
                            .ToListAsync(ct);
                        Domain.Postings.Posting? match = null;
                        if (candidates.Any())
                        {
                            match = candidates
                                .OrderBy(p => Math.Abs((p.BookingDate - newContactPost.BookingDate).Days))
                                .First();
                        }

                        if (match != null)
                        {
                            var bankMatch = await _db.Postings.FirstOrDefaultAsync(p => p.GroupId == match.GroupId && p.Kind == PostingKind.Bank, ct);
                            var bankNew = _db.Postings.Local.FirstOrDefault(p => p.GroupId == gid && p.Kind == PostingKind.Bank);
                            if (bankMatch != null && bankNew != null && bankMatch.AccountId != bankNew.AccountId)
                            {
                                if (match.SavingsPlanId == null && newContactPost.SavingsPlanId == null)
                                {
                                    match.SetLinkedPosting(newContactPost.Id);
                                    newContactPost.SetLinkedPosting(match.Id);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to attempt linking self-transfer posting for entry {EntryId}", e.Id);
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

                await TryAutoLinkIbanToCollectionAccountAsync(account, draft.AccountName, ct);
            }

            _reportCacheService?.EnqueueBudgetReportCacheRefresh(ownerUserId);
            await InvalidatePortfolioAnalysisReportCacheAfterBookingAsync(ownerUserId, account, createdPortfolioSecurityPostings, ct);

            return new BookingResult(true, false, validation, null, toBook.Count, await GetNextStatementDraftAsync(draft))
            {
                BudgetImpactSummary = budgetImpactSummary
            };
        }

        // FULL BOOKING: commit whole draft
        draft.MarkCommitted();
        await _db.SaveChangesAsync(ct);
        if (_attachments != null)
        {
            await _attachments.ReassignAsync(AttachmentEntityKind.StatementDraft, draft.Id, AttachmentEntityKind.Account, account.Id, ownerUserId, ct);
        }

        await TryAutoLinkIbanToCollectionAccountAsync(account, draft.AccountName, ct);

        _reportCacheService?.EnqueueBudgetReportCacheRefresh(ownerUserId);

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

        await InvalidatePortfolioAnalysisReportCacheAfterBookingAsync(ownerUserId, account, createdPortfolioSecurityPostings, ct);

        return new BookingResult(true, false, validation, null, toBook.Count, await GetNextStatementDraftAsync(draft))
        {
            BudgetImpactSummary = budgetImpactSummary
        };
    }

    /// <summary>
    /// If the account is a collection account and the IBAN from the statement draft is not yet
    /// in the linked IBANs list, adds it automatically. Errors are swallowed to avoid disrupting booking.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="statementIban">The statement iban.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task TryAutoLinkIbanToCollectionAccountAsync(Account account, string? statementIban, CancellationToken ct)
    {
        if (!account.IsCollectionAccount) return;
        if (string.IsNullOrWhiteSpace(statementIban)) return;

        var iban = statementIban.Replace(" ", "").Trim();
        if (string.IsNullOrWhiteSpace(iban)) return;

        // Do not add if it is the account's own IBAN
        if (string.Equals(account.Iban, iban, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            bool alreadyLinked = await _db.AccountLinkedIbans
                .AsNoTracking()
                .AnyAsync(li => li.AccountId == account.Id && li.Iban == iban, ct);

            if (!alreadyLinked)
            {
                _db.AccountLinkedIbans.Add(new AccountLinkedIban(account.Id, iban));
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Auto-linked IBAN {Iban} to collection account {AccountId} after booking.",
                    iban, account.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-link IBAN {Iban} to collection account {AccountId}.", iban, account.Id);
        }
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

    /// <summary>
    /// Deletes an entry from a draft.
    /// </summary>
    /// <param name="draftId">The draft id.</param>
    /// <param name="entryId">The entry id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
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

    /// <summary>
    /// Deletes all open drafts for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    public async Task<int> DeleteAllAsync(Guid ownerUserId, CancellationToken ct)
    {
        var openIds = await _db.StatementDrafts.Where(d => d.OwnerUserId == ownerUserId && d.Status == StatementDraftStatus.Draft).Select(d => d.Id).ToListAsync(ct);
        if (openIds.Count == 0) { return 0; }
        await _db.StatementDraftEntries.Where(e => openIds.Contains(e.DraftId)).ExecuteDeleteAsync(ct);
        var removed = await _db.StatementDrafts.Where(d => openIds.Contains(d.Id)).ExecuteDeleteAsync(ct);
        return removed;
    }

    private async Task BookSplitDraftGroupAsync(Guid representativeSplitDraftId, Guid ownerUserId, Guid parentDraftId, Contact self, Account account, CancellationToken ct, HashSet<Guid> visited, DateTime? overrideBookingDate = null, Guid? parentBankPostingId = null, Guid? parentContactPostingId = null)
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
            var draft = childDraft;
            foreach (var ce in childDraft.Entries)
            {
                if (ce.ContactId == null) { continue; }
                var contact = await _db.Contacts.FirstOrDefaultAsync(x => x.Id == ce.ContactId && x.OwnerUserId == ownerUserId, ct);
                if (contact == null) { continue; }

                async Task<(Guid groupId, Guid bankId, Guid contactId)> CreateBankAndContactAsync(StatementDraftEntry e, decimal amount, DateTime bookingDate, DateTime valutaDate)
                {
                    var gid = Guid.NewGuid();
                    var bank = new Domain.Postings.Posting(e.Id, PostingKind.Bank, account.Id, null, null, null, bookingDate, valutaDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                    _db.Postings.Add(bank); await UpsertAggregatesAsync(bank, ct);
                    var contactPosting = new Domain.Postings.Posting(e.Id, PostingKind.Contact, null, e.ContactId, null, null, bookingDate, valutaDate, amount, e.Subject, e.RecipientName, e.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                    _db.Postings.Add(contactPosting); await UpsertAggregatesAsync(contactPosting, ct);

                    if (amount == 0m)
                    {
                        bank.SetOriginalAmount(e.Amount);
                        contactPosting.SetOriginalAmount(e.Amount);
                    }

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
                async Task CreateSecurityAsync(StatementDraftEntry e, Guid gid, DateTime bookingDate, DateTime valutaDate)
                {
                    if (e.SecurityId == null) { return; }
                    var fee = Math.Abs(e.SecurityFeeAmount ?? 0m); var tax = Math.Abs(e.SecurityTaxAmount ?? 0m);
                    if (e.Amount < 0) { fee = -fee; tax = -tax; }
                    var factor = e.SecurityTransactionType switch { SecurityTransactionType.Buy => 1, SecurityTransactionType.Sell => -1, SecurityTransactionType.Dividend => -1, _ => -1 };
                    var tradeAmt = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.Amount - fee - tax, SecurityTransactionType.Sell => e.Amount + fee + tax, SecurityTransactionType.Dividend => e.Amount + fee + tax, _ => e.Amount + fee + tax };
                    var sub = e.SecurityTransactionType switch { SecurityTransactionType.Buy => SecurityPostingSubType.Buy, SecurityTransactionType.Sell => SecurityPostingSubType.Sell, SecurityTransactionType.Dividend => SecurityPostingSubType.Dividend, _ => SecurityPostingSubType.Buy };
                    decimal? qty = e.SecurityTransactionType switch { SecurityTransactionType.Buy => e.SecurityQuantity, SecurityTransactionType.Sell => e.SecurityQuantity.HasValue ? -Math.Abs(e.SecurityQuantity.Value) : null, SecurityTransactionType.Dividend => null, _ => e.SecurityQuantity };
                    var main = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, bookingDate, valutaDate, tradeAmt, e.Subject, e.RecipientName, e.BookingDescription, sub, qty).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                    _db.Postings.Add(main); await UpsertAggregatesAsync(main, ct);
                    if (fee != 0m)
                    {
                        var feeP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, bookingDate, valutaDate, factor * fee, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Fee, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                        _db.Postings.Add(feeP); await UpsertAggregatesAsync(feeP, ct);
                    }
                    if (tax != 0m)
                    {
                        var taxP = new Domain.Postings.Posting(e.Id, PostingKind.Security, null, null, null, e.SecurityId, bookingDate, valutaDate, factor * tax, e.Subject, e.RecipientName, e.BookingDescription, SecurityPostingSubType.Tax, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                        _db.Postings.Add(taxP); await UpsertAggregatesAsync(taxP, ct);
                    }
                }
                var effectiveBookingDate = overrideBookingDate ?? ce.BookingDate;
                var effectiveValutaDate = overrideBookingDate.HasValue ? ce.BookingDate : (ce.ValutaDate ?? ce.BookingDate);

                if (contact.IsPaymentIntermediary && ce.SplitDraftId != null)
                {
                    var (gidZero, bankId, contactId) = await CreateBankAndContactAsync(ce, 0m, effectiveBookingDate, effectiveValutaDate);
                    await CreateSecurityAsync(ce, gidZero, effectiveBookingDate, effectiveValutaDate);

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

                    await BookSplitDraftGroupAsync(ce.SplitDraftId.Value, ownerUserId, childDraft.Id, self, account, ct, visited, overrideBookingDate, bankId, contactId);
                    await MarkReportCacheForEntryAsync(ce, ct);
                }
                else
                {
                    var (gid, bankId, contactId) = await CreateBankAndContactAsync(ce, ce.Amount, effectiveBookingDate, effectiveValutaDate);
                    Guid? spId = null;
                    if (ce.SavingsPlanId != null && ce.ContactId == self.Id)
                    {
                        var sp = new Domain.Postings.Posting(ce.Id, PostingKind.SavingsPlan, null, null, ce.SavingsPlanId, null, effectiveBookingDate, effectiveValutaDate, -ce.Amount, ce.Subject, ce.RecipientName, ce.BookingDescription, null).SetGroup(gid).SetIsPreliminary(draft.IsPreliminary);
                        _db.Postings.Add(sp); await UpsertAggregatesAsync(sp, ct);
                        if (parentBankPostingId.HasValue) sp.SetParent(parentBankPostingId.Value);
                        if (parentContactPostingId.HasValue) sp.SetParent(parentContactPostingId.Value);
                        spId = sp.Id;
                    }
                    await CreateSecurityAsync(ce, gid, effectiveBookingDate, effectiveValutaDate);

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

                    // Attempt to link contact postings for self-transfers (split branch)
                    try
                    {
                        var newContactPost = _db.Postings.Local.FirstOrDefault(p => p.SourceId == ce.Id && p.Kind == PostingKind.Contact && p.GroupId == gid);
                        if (newContactPost != null && newContactPost.LinkedPostingId == null && newContactPost.ContactId == self.Id)
                        {
                            var candidates = await _db.Postings
                                .Where(p => p.Kind == PostingKind.Contact
                                            && p.ContactId == newContactPost.ContactId
                                            //&& p.Amount == -newContactPost.Amount
                                            && p.LinkedPostingId == null
                                            && p.SourceId != newContactPost.SourceId
                                            && p.Subject == newContactPost.Subject)
                                 .ToListAsync(ct);
                            Domain.Postings.Posting? match = null;
                            if (candidates.Any())
                            {
                                match = candidates
                                    .OrderBy(p => Math.Abs((p.BookingDate - newContactPost.BookingDate).Days))
                                    .First();
                            }

                            if (match != null)
                            {
                                var bankMatch = await _db.Postings.FirstOrDefaultAsync(p => p.GroupId == match.GroupId && p.Kind == PostingKind.Bank, ct);
                                var bankNew = _db.Postings.Local.FirstOrDefault(p => p.GroupId == gid && p.Kind == PostingKind.Bank);
                                if (bankMatch != null && bankNew != null && bankMatch.AccountId != bankNew.AccountId)
                                {
                                    if (match.SavingsPlanId == null && newContactPost.SavingsPlanId == null)
                                    {
                                        match.SetLinkedPosting(newContactPost.Id);
                                        newContactPost.SetLinkedPosting(match.Id);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to attempt linking self-transfer posting for split entry {EntryId}", ce.Id);
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

    /// <summary>
    /// Returns the identifiers of the previous and next drafts in the upload group of the specified draft.
    /// </summary>
    /// <param name="draftId">The identifier of the draft to check.</param>
    /// <param name="ownerUserId">The owner user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A tuple with the previous and next draft identifiers, or null if not applicable.</returns>
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

    /// <summary>
    /// Returns the sum of amounts in the split group of the specified draft, or null if not a split draft.
    /// </summary>
    /// <param name="splitDraftId">The identifier of the split draft.</param>
    /// <param name="ownerUserId">The owner user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The sum of amounts in the split group, or null.</returns>
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

    /// <summary>
    /// Resets a draft entry that was marked as duplicate, restoring its status to open and clearing certain fields.
    /// </summary>
    /// <param name="draftId">The draft identifier.</param>
    /// <param name="entryId">The entry identifier.</param>
    /// <param name="ownerUserId">The owner user identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated draft entry DTO, or null if not found.</returns>
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
                entry.EntryNumber,
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
            entry.EntryNumber,
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

    /// <summary>
    /// Sets the description of a draft and re-classifies the draft to update metadata.
    /// </summary>
    /// <param name="draftId">The identifier of the draft.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="description">The new description for the draft.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated draft DTO, or null if not found.</returns>
    public async Task<StatementDraftDto?> SetDescriptionAsync(Guid draftId, Guid ownerUserId, string? description, CancellationToken ct)
    {
        var draft = await _db.StatementDrafts.Include(d => d.Entries)
            .FirstOrDefaultAsync(d => d.Id == draftId && d.OwnerUserId == ownerUserId, ct);
        if (draft == null || draft.Status != StatementDraftStatus.Draft) { return null; }

        draft.Description = string.IsNullOrWhiteSpace(description) ? null : description;

        // Re-run classification to refresh derived metadata if needed
        await ClassifyInternalAsync(draft, null, ownerUserId, ct);
        await _db.SaveChangesAsync(ct);
        return await GetDraftAsync(draft.Id, ownerUserId, ct);
    }
}
