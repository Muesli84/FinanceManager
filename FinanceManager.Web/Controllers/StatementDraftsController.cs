using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Attachments;
using FinanceManager.Application.Common;
using FinanceManager.Application.Contacts;
using FinanceManager.Application.Exceptions;
using FinanceManager.Application.Savings;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Statements;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Statements;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages bank statement draft lifecycle: upload/import (including splitting), listing, classification,
/// editing entries, validation, booking (single or mass), and attachment download of original files.
/// Provides background task endpoints for mass classification and mass booking operations.
/// </summary>
[ApiController]
[Route("api/statement-drafts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class StatementDraftsController : ControllerBase
{
    private const string Origin = "API_StatementDraft";

    private readonly IStatementDraftService _drafts;
    private readonly ICurrentUserService _current;
    private readonly ILogger<StatementDraftsController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;
    private readonly IBackgroundTaskManager _taskManager; // unified background task system
    private readonly IAttachmentService _attachments; // new
    private readonly IBudgetImpactEvaluationService? _budgetImpact;
    private readonly IMassImportOrchestrator? _massImportOrchestrator;

    /// <summary>
    /// Initializes a new instance of <see cref="StatementDraftsController"/>.
    /// </summary>
    /// <param name="drafts">Service for managing statement drafts.</param>
    /// <param name="current">Service providing the current authenticated user context.</param>
    /// <param name="logger">Logger instance for the controller.</param>
    /// <param name="localizer">Localizer for i18n support.</param>
    /// <param name="taskManager">Background task manager used to enqueue/inspect background jobs.</param>
    /// <param name="attachments">Attachment service used to list and download attachments.</param>
    /// <param name="budgetImpact">Optional service used to evaluate the budget impact of bookings; may be null when the feature is disabled.</param>
    /// <param name="massImportOrchestrator">Optional orchestrator used to coordinate mass import of statement drafts; may be null when the feature is disabled.</param>
    public StatementDraftsController(
        IStatementDraftService drafts,
        ICurrentUserService current,
        ILogger<StatementDraftsController> logger,
        IStringLocalizer<Controller> localizer,
        IBackgroundTaskManager taskManager,
        IAttachmentService attachments,
        IBudgetImpactEvaluationService? budgetImpact = null,
        IMassImportOrchestrator? massImportOrchestrator = null)
    {
        _drafts = drafts;
        _current = current;
        _logger = logger;
        _localizer = localizer;
        _taskManager = taskManager;
        _attachments = attachments;
        _massImportOrchestrator = massImportOrchestrator;
        _budgetImpact = budgetImpact;
    }

    /// <summary>
    /// Lists open (not booked / cancelled) statement drafts with paging (max 3 per page).
    /// </summary>
    /// <param name="skip">Items to skip for paging.</param>
    /// <param name="take">Items to take (1..3).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="StatementDraftDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<StatementDraftDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenAsync([FromQuery] int skip = 0, [FromQuery] int take = 3, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 3);
        var drafts = await _drafts.GetOpenDraftsAsync(_current.UserId, skip, take, ct);
        return Ok(drafts);
    }

    /// <summary>
    /// Returns the total number of open drafts for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with an object containing the count.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOpenCountAsync(CancellationToken ct)
    {
        var count = await _drafts.GetOpenDraftsCountAsync(_current.UserId, ct);
        return Ok(new { count });
    }

    /// <summary>
    /// Creates a new preliminary (provisional) statement draft for the specified bank account.
    /// </summary>
    /// <param name="body">Request containing the account id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with the created <see cref="StatementDraftDto"/>; 400 when the request is invalid; 404 when the account does not exist.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("preliminary")]
    [ProducesResponseType(typeof(StatementDraftDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreatePreliminaryDraftAsync([FromBody] CreatePreliminaryStatementDraftRequest body, CancellationToken ct)
    {
        if (body.AccountId == Guid.Empty)
        {
            return BadRequest(new { error = "Err_Invalid_AccountId", message = "Account id is required" });
        }

        var pagesLocalizer = HttpContext.RequestServices.GetRequiredService<IStringLocalizer<FinanceManager.Web.Pages>>();
        var dateText = DateTime.Today.ToString("d", System.Globalization.CultureInfo.CurrentCulture);
        var description = pagesLocalizer["StatementDraft_Description_Preliminary", dateText].Value;

        var draft = await _drafts.CreatePreliminaryDraftAsync(_current.UserId, body.AccountId, ct, description);
        if (draft == null)
        {
            return NotFound(new { error = "Err_NotFound", message = "Account not found" });
        }

        return CreatedAtRoute("GetStatementDraft", new { draftId = draft.DraftId }, draft);
    }

    /// <summary>
    /// Deletes all open drafts for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the number of deleted drafts.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpDelete("all")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteAllAsync(CancellationToken ct)
    {
        var removed = await _drafts.DeleteAllAsync(_current.UserId, ct);
        _logger.LogInformation("Deleted {Count} open statement drafts for user {UserId}", removed, _current.UserId);
        return Ok(new { deleted = removed });
    }

    /// <summary>
    /// Uploads a statement file (CSV/PDF etc.) and creates one or more draft records (supports split imports).
    /// Returns the first created draft plus optional import split metadata.
    /// </summary>
    /// <param name="file">Uploaded file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="StatementDraftUploadResult"/>, or 400 Bad Request when file is missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(10_000_000)]
    [ProducesResponseType(typeof(StatementDraftUploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAsync(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            var key = $"{Origin}_Err_Invalid_File";
            var entry = _localizer[key];
            var message = entry.ResourceNotFound ? "File required" : entry.Value;
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid_File", message));
        }

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        StatementDraftDto? firstDraft = null;
        await foreach (var draft in _drafts.CreateDraftAsync(_current.UserId, file.FileName, ms.ToArray(), ct)) { firstDraft ??= draft; }
        ImportSplitInfoDto? splitInfo = null;
        // NOTE: LastImportSplitInfo is an implementation detail; controller should not depend on Infrastructure types.
        // If this metadata is needed in the response, consider exposing it via IStatementDraftService.
        // if (_drafts is StatementDraftService impl && impl.LastImportSplitInfo != null)
        // {
        //     var info = impl.LastImportSplitInfo;
        //     splitInfo = new ImportSplitInfoDto(info.ConfiguredMode.ToString(), info.EffectiveMonthly, info.DraftCount, info.TotalMovements, info.MaxEntriesPerDraft, info.LargestDraftSize, info.MonthlyThreshold);
        // }
        return Ok(new StatementDraftUploadResult(firstDraft, splitInfo));
    }

    /// <summary>
    /// Analyzes or executes a mixed mass import batch from the start page.
    /// </summary>
    /// <param name="request">Batch request with files, dialog policy and optional user decisions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Batch analysis or execution result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("mass-import")]
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(MassImportBatchResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProcessMassImportAsync([FromBody] MassImportBatchRequestDto? request, CancellationToken ct)
    {
        if (request == null || request.Files == null || request.Files.Count == 0)
        {
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid_File", "At least one file is required."));
        }
        if (_massImportOrchestrator == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }

        var traceId = HttpContext.TraceIdentifier;
        var result = await _massImportOrchestrator.ProcessAsync(_current.UserId, request, traceId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns status of the background classification task (classify all drafts).
    /// </summary>
    /// <returns>200 OK with task run state and progress.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("classify/status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetClassifyStatus()
    {
        var task = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.ClassifyAllDrafts)
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault(t => t.Status is BackgroundTaskStatus.Running or BackgroundTaskStatus.Queued);
        if (task == null) { return Ok(new { running = false, processed = 0, total = 0, message = (string?)null }); }
        return Ok(new { running = task.Status == BackgroundTaskStatus.Running || task.Status == BackgroundTaskStatus.Queued, processed = task.Processed ?? 0, total = task.Total ?? 0, message = task.Message });
    }

    /// <summary>
    /// Enqueues classification of all open drafts if not already running.
    /// </summary>
    /// <returns>202 Accepted indicating the task was queued or already running.</returns>
    /// <response code="202">The HTTP 202 response.</response>
    [HttpPost("classify")]
    [ProducesResponseType(typeof(object), StatusCodes.Status202Accepted)]
    public IActionResult ClassifyAllAsync()
    {
        var existing = _taskManager.GetAll().FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.ClassifyAllDrafts && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
        if (existing != null) { return Accepted(new { running = true, processed = existing.Processed ?? 0, total = existing.Total ?? 0, message = existing.Message }); }
        var info = _taskManager.Enqueue(BackgroundTaskType.ClassifyAllDrafts, _current.UserId);
        _logger.LogInformation("Enqueued classification background task {TaskId} for user {UserId}", info.Id, _current.UserId);
        return Accepted(new { running = true, processed = 0, total = 0, message = "Queued" });
    }

    /// <summary>
    /// Returns status of mass booking background task.
    /// </summary>
    /// <returns>200 OK with <see cref="StatementDraftMassBookStatusDto"/> describing current task state.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("book-all/status")]
    [ProducesResponseType(typeof(StatementDraftMassBookStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetBookAllStatus()
    {
        var task = _taskManager.GetAll().Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts).OrderByDescending(t => t.EnqueuedUtc).FirstOrDefault(t => t.Status is BackgroundTaskStatus.Running or BackgroundTaskStatus.Queued);
        if (task == null) { return Ok(new StatementDraftMassBookStatusDto(false, 0, 0, 0, 0, 0, null, Array.Empty<StatementDraftMassBookIssueDto>())); }
        return Ok(new StatementDraftMassBookStatusDto(task.Status == BackgroundTaskStatus.Running || task.Status == BackgroundTaskStatus.Queued, task.Processed ?? 0, 0, task.Total ?? 0, task.Warnings, task.Errors, task.Message, Array.Empty<StatementDraftMassBookIssueDto>()));
    }

    /// <summary>
    /// Enqueues booking of all drafts (mass booking) unless already running.
    /// </summary>
    /// <param name="req">Mass booking options.</param>
    /// <returns>202 Accepted with initial task status.</returns>
    /// <response code="202">The HTTP 202 response.</response>
    [HttpPost("book-all")]
    [ProducesResponseType(typeof(StatementDraftMassBookStatusDto), StatusCodes.Status202Accepted)]
    public IActionResult BookAllAsync([FromBody] StatementDraftMassBookRequest req)
    {
        var existing = _taskManager.GetAll().FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
        if (existing != null) { return Accepted(new StatementDraftMassBookStatusDto(true, existing.Processed ?? 0, 0, existing.Total ?? 0, existing.Warnings, existing.Errors, existing.Message, Array.Empty<StatementDraftMassBookIssueDto>())); }
        var payload = new { req.IgnoreWarnings, req.AbortOnFirstIssue, req.BookEntriesIndividually };
        var info = _taskManager.Enqueue(BackgroundTaskType.BookAllDrafts, _current.UserId, payload, allowDuplicate: false);
        _logger.LogInformation("Enqueued booking background task {TaskId} for user {UserId}", info.Id, _current.UserId);
        return Accepted(new StatementDraftMassBookStatusDto(true, 0, 0, 0, 0, 0, "Queued", Array.Empty<StatementDraftMassBookIssueDto>()));
    }

    /// <summary>
    /// Attempts to cancel a running mass booking task.
    /// </summary>
    /// <returns>202 Accepted.</returns>
    /// <response code="202">The HTTP 202 response.</response>
    [HttpPost("book-all/cancel")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public IActionResult CancelBookAll()
    {
        var task = _taskManager.GetAll().FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts && t.Status == BackgroundTaskStatus.Running);
        if (task == null) { return Accepted(); }
        _taskManager.TryCancel(task.Id);
        return Accepted();
    }

    /// <summary>
    /// Gets header or full detail for a draft including neighbor draft ids.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="headerOnly">If true only header/entries metadata is returned.</param>
    /// <param name="src">Optional source hint.</param>
    /// <param name="fromEntryDraftId">Optional originating draft id (navigation aid).</param>
    /// <param name="fromEntryId">Optional originating entry id (navigation aid).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="StatementDraftDetailDto"/>, or 404 Not Found when draft is missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{draftId:guid}", Name = "GetStatementDraft")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid draftId, [FromQuery] bool headerOnly = false, [FromQuery] string? src = null, [FromQuery] Guid? fromEntryDraftId = null, [FromQuery] Guid? fromEntryId = null, CancellationToken ct = default)
    {
        StatementDraftDto? draft = headerOnly ? await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct) : await _drafts.GetDraftAsync(draftId, _current.UserId, ct);
        if (draft is null) { return NotFound(); }
        var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draftId, _current.UserId, ct);

        // Build symbol maps
        IReadOnlyDictionary<Guid, Guid?>? contactSymbols = null;
        IReadOnlyDictionary<Guid, Guid?>? planSymbols = null;
        IReadOnlyDictionary<Guid, string>? planNames = null;
        IReadOnlyDictionary<Guid, Guid?>? securitySymbols = null;
        IReadOnlyDictionary<Guid, string>? securityNames = null;
        IReadOnlyDictionary<Guid, string>? contactNames = null;
        Guid? accountBankContactId = null;
        Guid? selfContactId = null;
        try
        {
            var contactSvc = HttpContext.RequestServices.GetRequiredService<IContactService>();
            var categorySvc = HttpContext.RequestServices.GetRequiredService<IContactCategoryService>();
            var accountSvc = HttpContext.RequestServices.GetService<IAccountService>();
            var planSvc = HttpContext.RequestServices.GetRequiredService<ISavingsPlanService>();
            var securitySvc = HttpContext.RequestServices.GetRequiredService<ISecurityService>();
            var cMap = new Dictionary<Guid, Guid?>();
            var cNames = new Dictionary<Guid, string>();
            var pMap = new Dictionary<Guid, Guid?>();
            var pNames = new Dictionary<Guid, string>();
            var sMap = new Dictionary<Guid, Guid?>();
            var sNames = new Dictionary<Guid, string>();
            if (draft.DetectedAccountId.HasValue && accountSvc != null)
            {
                var account = await accountSvc.GetAsync(draft.DetectedAccountId.Value, _current.UserId, ct);
                accountBankContactId = account?.BankContactId;
            }
            accountBankContactId ??= draft.AccountBankContactId;

            var selfContacts = await contactSvc.ListAsync(_current.UserId, 0, 1, ContactType.Self, null, ct);
            selfContactId = selfContacts.FirstOrDefault()?.Id;

            foreach (var e in draft.Entries)
            {
                if (e.ContactId.HasValue)
                {
                    var c = await contactSvc.GetAsync(e.ContactId.Value, _current.UserId, ct);
                    Guid? symbol = c?.SymbolAttachmentId;
                    if (symbol == null && c?.CategoryId != null)
                    {
                        var cat = await categorySvc.GetAsync(c.CategoryId.Value, _current.UserId, ct);
                        symbol = cat?.SymbolAttachmentId;
                    }
                    cMap[e.Id] = symbol;
                    if (!string.IsNullOrWhiteSpace(c?.Name))
                    {
                        cNames[e.Id] = c.Name;
                    }
                }
                if (e.SavingsPlanId.HasValue)
                {
                    var p = await planSvc.GetAsync(e.SavingsPlanId.Value, _current.UserId, ct);
                    pMap[e.Id] = p?.SymbolAttachmentId;
                    if (p != null) { pNames[e.Id] = p.Name; }
                }
                if (e.SecurityId.HasValue)
                {
                    var s = await securitySvc.GetAsync(e.SecurityId.Value, _current.UserId, ct);
                    sMap[e.Id] = s?.SymbolAttachmentId;
                    if (s != null) { sNames[e.Id] = s.Name; }
                }
            }
            contactSymbols = cMap;
            contactNames = cNames;
            planSymbols = pMap;
            planNames = pNames;
            securitySymbols = sMap;
            securityNames = sNames;
        }
        catch { }

        var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId, contactSymbols, planSymbols, planNames, securitySymbols, securityNames, contactNames, accountBankContactId, selfContactId, draft.IsPreliminary);
        return Ok(dto);
    }

    /// <summary>
    /// Gets detailed information about a single draft entry including previous/next and split summary.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="StatementDraftEntryDetailDto"/> or 404 Not Found when entry not found.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{draftId:guid}/entries/{entryId:guid}")]
    [ProducesResponseType(typeof(StatementDraftEntryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var draft = await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct);
        if (draft is null && draftId == Guid.Empty)
            draft = await _drafts.FindDraftHeaderAsync(entryId, _current.UserId, ct);
        if (draft is null) return NotFound();

        var ordered = (await _drafts.GetDraftEntriesAsync(draft.DraftId, ct)).OrderBy(e => e.BookingDate).ThenBy(e => e.Id).ToList();
        var entry = await _drafts.GetDraftEntryAsync(draft.DraftId, entryId, ct);
        if (entry is null) { return NotFound(); }
        var index = ordered.FindIndex(e => e.Id == entryId);
        var prev = index > 0 ? ordered[index - 1].Id : (Guid?)null;
        var next = index < ordered.Count - 1 ? ordered[index + 1].Id : (Guid?)null;
        // Prefer the next entry that is Open or Announced. If none found, fall back to the immediate next entry regardless of status.
        var nextOpen = ordered.Skip(index + 1).FirstOrDefault(e => e.Status == StatementDraftEntryStatus.Open || e.Status == StatementDraftEntryStatus.Announced)?.Id
            ?? (index < ordered.Count - 1 ? ordered[index + 1].Id : (Guid?)null);
        decimal? splitSum = null; decimal? diff = null;
        if (entry.SplitDraftId != null)
        {
            splitSum = await _drafts.GetSplitGroupSumAsync(entry.SplitDraftId.Value, _current.UserId, ct);
            if (splitSum.HasValue) { diff = entry.Amount - splitSum.Value; }
        }
        Guid? bankContactId = null;
        if (draft!.DetectedAccountId.HasValue)
        {
            var accountService = HttpContext.RequestServices.GetRequiredService<IAccountService>();
            var account = await accountService.GetAsync(draft.DetectedAccountId.Value, _current.UserId, ct);
            bankContactId = account?.BankContactId;
        }
        var dto = new StatementDraftEntryDetailDto(draft.DraftId, draft.OriginalFileName, entry, prev, next, nextOpen, splitSum, diff, bankContactId);
        return Ok(dto);
    }

    /// <summary>
    /// Adds a new manual entry to a draft.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="req">Entry creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="StatementDraftDetailDto"/> or 400 Bad Request on validation errors.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("{draftId:guid}/entries")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddEntryAsync(Guid draftId, [FromBody] StatementDraftAddEntryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var draft = await _drafts.AddEntryAsync(draftId, _current.UserId, req.BookingDate, req.Amount, req.Subject, ct);
        if (draft is null) return NotFound();
        var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draft.DraftId, _current.UserId, ct);
        var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId);
        return Ok(dto);
    }

    /// <summary>
    /// Classifies draft entries (attempts to detect account, contacts etc.).
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="StatementDraftDetailDto"/>, or 400 Bad Request on error.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("{draftId:guid}/classify")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClassifyAsync(Guid draftId, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.ClassifyAsync(draftId, null, _current.UserId, ct);
            if (draft is null) return NotFound();
            var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draft.DraftId, _current.UserId, ct);
            var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId);
            return Ok(dto);
        }
        catch (Exception ex) { return BadRequest(ex); }
    }

    /// <summary>
    /// Sets the detected account for a draft.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="accountId">Account id to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="StatementDraftDetailDto"/>, or 404 Not Found when draft missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/account/{accountId:guid}")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAccountAsync(Guid draftId, Guid accountId, CancellationToken ct)
    {
        var draft = await _drafts.SetAccountAsync(draftId, _current.UserId, accountId, ct);
        if (draft is null) return NotFound();
        var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draft.DraftId, _current.UserId, ct);
        var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId);
        return Ok(dto);
    }

    /// <summary>
    /// Commits a draft by creating statement entries from its content (without booking them yet).
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="req">Commit options including target account and format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with commit result or 404 Not Found when draft missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/commit")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CommitAsync(Guid draftId, [FromBody] StatementDraftCommitRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _drafts.CommitAsync(draftId, _current.UserId, req.AccountId, req.Format, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (DomainValidationException dex)
        {
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, dex, _localizer));
        }
        catch (InvalidOperationException ioex)
        {
            const string code = "Err_InvalidState";
            var msgEntry = _localizer[$"{Origin}_{code}"];
            var message = msgEntry.ResourceNotFound ? ioex.Message : msgEntry.Value;
            return Conflict(ApiErrorDto.Create(Origin, code, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Commit statement draft failed {DraftId}", draftId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Sets the contact reference for a draft entry.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request body containing contact id to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the updated entry or 404 Not Found when draft/entry missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/contact")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntryContactAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetContactRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryContactAsync(draftId, entryId, body.ContactId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        var impact = _budgetImpact == null ? null : await _budgetImpact.EvaluateEntryImpactAsync(draftId, entryId, _current.UserId, ct);
        return Ok(entry with { BudgetImpact = impact });
    }

    /// <summary>
    /// Marks a draft entry as cost neutral or not.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing the cost-neutral flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry or 404 Not Found when draft/entry missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/costneutral")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntryCostNeutralAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetCostNeutralRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryCostNeutralAsync(draftId, entryId, body.IsCostNeutral, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    /// <summary>
    /// Associates a savings plan with a draft entry.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing savings plan id to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry or 404 Not Found when draft/entry missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/savingsplan")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntrySavingPlanAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetSavingsPlanRequest body, CancellationToken ct)
    {
        var draft = await _drafts.AssignSavingsPlanAsync(draftId, entryId, body.SavingsPlanId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        var impact = _budgetImpact == null ? null : await _budgetImpact.EvaluateEntryImpactAsync(draftId, entryId, _current.UserId, ct);
        return Ok(entry with { BudgetImpact = impact });
    }

    /// <summary>
    /// Assigns or clears a split draft group for a draft entry and returns updated split difference.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing split draft id or clear flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with split summary dto or 400 Bad Request on invalid operation.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/split")]
    [ProducesResponseType(typeof(FinanceManager.Shared.Dtos.Statements.StatementDraftSetEntrySplitDraftResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetEntrySplitDraftAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetSplitDraftRequest body, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.SetEntrySplitDraftAsync(draftId, entryId, body.SplitDraftId, _current.UserId, ct);
            if (draft == null)
            {
                return NotFound();
            }

            var entry = draft.Entries.First(e => e.Id == entryId);
            decimal? splitSum = null;
            decimal? diff = null;
            if (entry.SplitDraftId != null)
            {
                splitSum = await _drafts.GetSplitGroupSumAsync(entry.SplitDraftId.Value, _current.UserId, ct);
                if (splitSum.HasValue)
                {
                    diff = entry.Amount - splitSum.Value;
                }
            }

            var result = new FinanceManager.Shared.Dtos.Statements.StatementDraftSetEntrySplitDraftResultDto(entry, splitSum, diff);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            const string code = "Err_InvalidState";
            var msgEntry = _localizer[$"{Origin}_{code}"];
            var message = msgEntry.ResourceNotFound ? ex.Message : msgEntry.Value;
            return Conflict(ApiErrorDto.Create(Origin, code, message));
        }
    }

    /// <summary>
    /// Cancels (removes) a draft.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success or 404 Not Found when draft missing.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{draftId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelAsync(Guid draftId, CancellationToken ct)
    {
        var ok = await _drafts.CancelAsync(draftId, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Downloads the original uploaded statement file for a draft.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the file content as a downloadable file, or 404 Not Found when not available.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{draftId:guid}/file")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadOriginalAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        // ListAsync signature expects additional optional parameters (categoryId, isUrl, filter)
        var list = await _attachments.ListAsync(_current.UserId, AttachmentEntityKind.StatementDraft, draftId, 0, 1, /*categoryId*/ null, /*isUrl*/ false, /*filter*/ null, ct);
        var fileMeta = list.FirstOrDefault();
        if (fileMeta == null) { return NotFound(); }
        var payload = await _attachments.DownloadAsync(_current.UserId, fileMeta.Id, ct);
        if (payload == null) { return NotFound(); }
        var downloaded = payload.Value;
        var content = downloaded.Item1;
        var fileName = downloaded.Item2;
        var contentType = downloaded.Item3;
        return File(content, string.IsNullOrWhiteSpace(contentType) ? MediaTypeNames.Application.Octet : contentType, fileName);
    }

    /// <summary>
    /// Updates core fields of a draft entry (dates, amount, textual fields).
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing core field values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry or 404 Not Found when missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/edit-core")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEntryCoreAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftUpdateEntryCoreRequest body, CancellationToken ct)
    {
        var updated = await _drafts.UpdateEntryCoreAsync(draftId, entryId, _current.UserId, body.BookingDate, body.ValutaDate, body.Amount, body.Subject, body.RecipientName, body.CurrencyCode, body.BookingDescription, ct);
        return updated == null ? NotFound() : Ok(updated);
    }

    /// <summary>
    /// Sets security metadata for a draft entry (transaction type, quantity, fees, taxes).
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing security metadata.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry or 404 Not Found when missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/security")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntrySecurityAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetEntrySecurityRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntrySecurityAsync(draftId, entryId, body.SecurityId, body.TransactionType, body.Quantity, body.FeeAmount, body.TaxAmount, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    /// <summary>
    /// Configures whether a savings plan is archived automatically when booking this entry.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing the archive-on-booking flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry or 404 Not Found when missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/savingsplan/archive-on-booking")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetEntryArchiveSavingsPlanOnBookingAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSetArchiveSavingsPlanOnBookingRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryArchiveSavingsPlanOnBookingAsync(draftId, entryId, body.ArchiveOnBooking, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    /// <summary>
    /// Validates a draft (all entries) and returns validation messages.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="DraftValidationResultDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("{draftId:guid}/validate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateAsync(Guid draftId, CancellationToken ct)
    {
        var result = await _drafts.ValidateAsync(draftId, null, _current.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Validates a single draft entry and returns validation messages.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="DraftValidationResultDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("{draftId:guid}/entries/{entryId:guid}/validate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var result = await _drafts.ValidateAsync(draftId, entryId, _current.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Books a draft (all entries) creating postings; warns or errors based on validation outcome.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="forceWarnings">When true warnings are ignored and booking proceeds if possible.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="BookingResult"/>, 400 Bad Request or 428 Precondition Required depending on validation.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    /// <response code="428">The HTTP 428 response.</response>
    [HttpPost("{draftId:guid}/book")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BookAsync(Guid draftId, [FromQuery] bool forceWarnings = false, CancellationToken ct = default)
    {
        var result = await _drafts.BookAsync(draftId, null, _current.UserId, forceWarnings, ct);
        var conflict = TryCreateBookingConflictProblem(result);
        if (conflict != null) { return conflict; }
        if (!result.Success && result.Validation.Messages.Any(m => m.Severity == "Error")) { return BadRequest(result); }
        if (!result.Success && result.HasWarnings) { return StatusCode(StatusCodes.Status428PreconditionRequired, result); }
        return Ok(result);
    }

    /// <summary>
    /// Books a single draft entry creating postings; warns or errors based on validation outcome.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="forceWarnings">When true warnings are ignored and booking proceeds if possible.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="BookingResult"/>, 400 Bad Request or 428 Precondition Required depending on validation.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    /// <response code="428">The HTTP 428 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/book")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> BookEntryAsync(Guid draftId, Guid entryId, [FromQuery] bool forceWarnings = false, CancellationToken ct = default)
    {
        var result = await _drafts.BookAsync(draftId, entryId, _current.UserId, forceWarnings, ct);
        var conflict = TryCreateBookingConflictProblem(result);
        if (conflict != null) { return conflict; }
        if (!result.Success && result.Validation.Messages.Any(m => m.Severity == "Error")) { return BadRequest(result); }
        if (!result.Success && result.HasWarnings) { return StatusCode(StatusCodes.Status428PreconditionRequired, result); }
        return Ok(result);
    }

    private ObjectResult? TryCreateBookingConflictProblem(BookingResult result)
    {
        if (result.Success || string.IsNullOrWhiteSpace(result.ErrorCode))
        {
            return null;
        }

        if (!string.Equals(result.ErrorCode, "BOOKING_IN_PROGRESS", StringComparison.Ordinal) &&
            !string.Equals(result.ErrorCode, "BOOKING_ALREADY_PROCESSED", StringComparison.Ordinal))
        {
            return null;
        }

        var detail = result.Validation.Messages.FirstOrDefault(m => string.Equals(m.Code, result.ErrorCode, StringComparison.Ordinal))?.Message
            ?? "Booking request rejected.";

        var problem = new ProblemDetails
        {
            Type = $"https://financemanager/errors/{result.ErrorCode.ToLowerInvariant().Replace('_', '-')}",
            Title = string.Equals(result.ErrorCode, "BOOKING_IN_PROGRESS", StringComparison.Ordinal)
                ? "Booking is already running"
                : "Booking already processed",
            Status = StatusCodes.Status409Conflict,
            Detail = detail
        };

        problem.Extensions["code"] = result.ErrorCode;
        problem.Extensions["retryable"] = result.Retryable ?? false;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        return StatusCode(StatusCodes.Status409Conflict, problem);
    }

    /// <summary>
    /// Saves all entry-related fields (contact, cost-neutral, savings plan, security) in one operation.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="body">Request containing fields to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated entry DTO, 400 Bad Request for domain validation, or 500 on unexpected errors.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/save-all")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveEntryAllAsync(Guid draftId, Guid entryId, [FromBody] StatementDraftSaveEntryAllRequest body, CancellationToken ct)
    {
        // If split request provided, route to dedicated service method and return minimal shape like other endpoints
        if (body.SplitDraftId.HasValue || (body.ClearSplit.HasValue && body.ClearSplit.Value))
        {
            var newSplitId = body.ClearSplit == true ? (Guid?)null : body.SplitDraftId;
            var draft = await _drafts.SetEntrySplitDraftAsync(draftId, entryId, newSplitId, _current.UserId, ct);
            if (draft == null)
            {
                return NotFound();
            }

            var entry = draft.Entries.First(e => e.Id == entryId);
            decimal? splitSum = null;
            decimal? diff = null;
            if (entry.SplitDraftId != null)
            {
                splitSum = await _drafts.GetSplitGroupSumAsync(entry.SplitDraftId.Value, _current.UserId, ct);
                if (splitSum.HasValue)
                {
                    diff = entry.Amount - splitSum.Value;
                }
            }

            return Ok(new FinanceManager.Shared.Dtos.Statements.StatementDraftSetEntrySplitDraftResultDto(entry, splitSum, diff));
        }

        try
        {
            var dto = await _drafts.SaveEntryAllAsync(
                draftId,
                entryId,
                _current.UserId,
                body.ContactId,
                body.IsCostNeutral,
                body.SavingsPlanId,
                body.ArchiveOnBooking,
                body.SecurityId,
                body.TransactionType,
                body.Quantity,
                body.FeeAmount,
                body.TaxAmount,
                ct);

            if (dto == null) { return NotFound(); }
            var impact = _budgetImpact == null ? null : await _budgetImpact.EvaluateEntryImpactAsync(draftId, entryId, _current.UserId, ct);
            return Ok(dto with { BudgetImpact = impact });
        }
        catch (DomainValidationException dex)
        {
            _logger.LogInformation("Domain validation when saving entry {EntryId} in draft {DraftId}: {Code} - {Message}", entryId, draftId, dex.Code, dex.Message);
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, dex, _localizer));
        }
        catch (InvalidOperationException ioex)
        {
            _logger.LogInformation(ioex, "Invalid operation when saving entry {EntryId} in draft {DraftId}", entryId, draftId);
            const string code = "Err_InvalidState";
            var msgEntry = _localizer[$"{Origin}_{code}"];
            var message = msgEntry.ResourceNotFound ? ioex.Message : msgEntry.Value;
            return Conflict(ApiErrorDto.Create(Origin, code, message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in SaveEntryAllAsync for draft {DraftId} entry {EntryId}", draftId, entryId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Permanently deletes a draft entry.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success or 404 Not Found when draft/entry missing.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{draftId:guid}/entries/{entryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var ok = await _drafts.DeleteEntryAsync(draftId, entryId, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Resets duplicate detection flags for a draft entry.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the reset entry or 404 Not Found when draft/entry missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/reset-duplicate")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> ResetDuplicateAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var dto = await _drafts.ResetDuplicateEntryAsync(draftId, entryId, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Classifies a single entry (heuristics / ML) and returns updated draft detail.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="entryId">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="StatementDraftDetailDto"/>, or 400 Bad Request on error.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("{draftId:guid}/entries/{entryId:guid}/classify-entry")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ClassifyEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.ClassifyAsync(draftId, entryId, _current.UserId, ct);
            if (draft is null) return NotFound();
            var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draft.DraftId, _current.UserId, ct);
            var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId);
            return Ok(dto);
        }
        catch (Exception ex)
        {
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid", ex.Message));
        }
    }

    /// <summary>
    /// Creates an empty statement draft (no file) for the current user.
    /// </summary>
    /// <param name="fileName">Optional initial file name for the draft.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with the new draft <see cref="StatementDraftDetailDto"/>.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromQuery] string? fileName = null, CancellationToken ct = default)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "new-draft" : fileName;
        var draft = await _drafts.CreateEmptyDraftAsync(_current.UserId, name, ct);
        if (draft == null) return BadRequest();
        // Return 201 Created with location header pointing to GetAsync via named route
        return CreatedAtRoute("GetStatementDraft", new { draftId = draft.DraftId }, draft);
    }

    /// <summary>
    /// Sets the description for a draft.
    /// </summary>
    /// <param name="draftId">Draft id.</param>
    /// <param name="description">Description text.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="StatementDraftDetailDto"/>, or 404 Not Found when draft missing.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost("{draftId:guid}/description")]
    [ProducesResponseType(typeof(StatementDraftDetailDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDescriptionAsync(Guid draftId, [FromBody] string? description, CancellationToken ct)
    {
        var draft = await _drafts.SetDescriptionAsync(draftId, _current.UserId, description, ct);
        if (draft == null) return NotFound();
        var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draft.DraftId, _current.UserId, ct);
        var dto = new StatementDraftDetailDto(draft.DraftId, draft.OriginalFileName, draft.Description, draft.DetectedAccountId, draft.Status, draft.TotalAmount, draft.IsSplitDraft, draft.ParentDraftId, draft.ParentEntryId, draft.ParentEntryAmount, draft.UploadGroupId, draft.Entries, neighbors.prevId, neighbors.nextId);
        return Ok(dto);
    }
}
