using FinanceManager.Application;
using FinanceManager.Application.Accounts;
using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Statements;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Accounts;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mime;
using FinanceManager.Domain.Securities;
using FinanceManager.Web.Services;
using System.Text.Json;
using FinanceManager.Infrastructure.Statements; // for ImportSplitInfo
using FinanceManager.Application.Attachments; // new
using FinanceManager.Domain.Attachments; // new

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/statement-drafts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class StatementDraftsController : ControllerBase
{
    private readonly IStatementDraftService _drafts;
    private readonly ICurrentUserService _current;
    private readonly ILogger<StatementDraftsController> _logger;
    private readonly IBackgroundTaskManager _taskManager; // unified background task system
    private readonly IAttachmentService _attachments; // new

    public StatementDraftsController(
        IStatementDraftService drafts,
        ICurrentUserService current,
        ILogger<StatementDraftsController> logger,
        IBackgroundTaskManager taskManager,
        IAttachmentService attachments)
    {
        _drafts = drafts;
        _current = current;
        _logger = logger;
        _taskManager = taskManager;
        _attachments = attachments;
    }

    // Minimal no-op implementation used by test ctor
    private sealed class NoopAttachmentService : IAttachmentService
    {
        public Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct)
            => Task.FromResult(new AttachmentDto(Guid.Empty, (short)kind, entityId, fileName, contentType, 0L, categoryId, DateTime.UtcNow, false));
        public Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct)
            => Task.FromResult(new AttachmentDto(Guid.Empty, (short)kind, entityId, fileName ?? url, "text/uri-list", 0L, categoryId, DateTime.UtcNow, true));
        public Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<AttachmentDto>>(Array.Empty<AttachmentDto>());
        public Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct)
            => Task.FromResult<(Stream, string, string)?>(null);
        public Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct) => Task.FromResult(false);
        public Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct) => Task.CompletedTask;
    }

    public sealed record UploadRequest([Required] string FileName);

    public sealed record UploadResult(StatementDraftDto? FirstDraft, object? SplitInfo);

    [HttpGet]
    public async Task<IActionResult> GetOpenAsync([FromQuery] int skip = 0, [FromQuery] int take = 3, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 3);
        var drafts = await _drafts.GetOpenDraftsAsync(_current.UserId, skip, take, ct);
        return Ok(drafts);
    }

    // NEW: delete all open drafts
    [HttpDelete("all")]
    public async Task<IActionResult> DeleteAllAsync(CancellationToken ct)
    {
        var removed = await _drafts.DeleteAllAsync(_current.UserId, ct);
        _logger.LogInformation("Deleted {Count} open statement drafts for user {UserId}", removed, _current.UserId);
        return Ok(new { deleted = removed });
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) { return BadRequest(new { error = "File required" }); }
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        StatementDraftDto? firstDraft = null;
        await foreach (var draft in _drafts.CreateDraftAsync(_current.UserId, file.FileName, ms.ToArray(), ct))
        {
            firstDraft ??= draft;
        }
        // Try to get split info (service is implementation instance)
        object? splitInfo = null;
        if (_drafts is StatementDraftService impl && impl.LastImportSplitInfo != null)
        {
            var info = impl.LastImportSplitInfo;
            splitInfo = new
            {
                Mode = info.ConfiguredMode.ToString(),
                info.EffectiveMonthly,
                info.DraftCount,
                info.TotalMovements,
                info.MaxEntriesPerDraft,
                info.LargestDraftSize,
                info.MonthlyThreshold
            };
        }
        return Ok(new UploadResult(firstDraft, splitInfo));
    }

    // Classification status via background task queue
    [HttpGet("classify/status")]
    public IActionResult GetClassifyStatus()
    {
        var task = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.ClassifyAllDrafts)
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault(t => t.Status is BackgroundTaskStatus.Running or BackgroundTaskStatus.Queued);

        if (task == null)
        {
            return Ok(new { running = false, processed = 0, total = 0, message = (string?)null });
        }

        return Ok(new
        {
            running = task.Status == BackgroundTaskStatus.Running || task.Status == BackgroundTaskStatus.Queued,
            processed = task.Processed ?? 0,
            total = task.Total ?? 0,
            message = task.Message
        });
    }

    [HttpPost("classify")]
    public IActionResult ClassifyAllAsync()
    {
        var existing = _taskManager.GetAll()
            .FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.ClassifyAllDrafts && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
        if (existing != null)
        {
            return Accepted(new { running = true, processed = existing.Processed ?? 0, total = existing.Total ?? 0, message = existing.Message });
        }

        var info = _taskManager.Enqueue(BackgroundTaskType.ClassifyAllDrafts, _current.UserId);
        _logger.LogInformation("Enqueued classification background task {TaskId} for user {UserId}", info.Id, _current.UserId);
        return Accepted(new { running = true, processed = 0, total = 0, message = "Queued" });
    }

    [HttpGet("book-all/status")]
    public IActionResult GetBookAllStatus()
    {
        var task = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts)
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault(t => t.Status is BackgroundTaskStatus.Running or BackgroundTaskStatus.Queued);
        if (task == null)
        {
            return Ok(new { running = false, processed = 0, failed = 0, total = 0, warnings = 0, errors = 0, message = (string?)null, issues = Array.Empty<object>() });
        }
        return Ok(new { running = task.Status == BackgroundTaskStatus.Running, processed = task.Processed ?? 0, failed = 0, total = task.Total ?? 0, warnings = task.Warnings, errors = task.Errors, message = task.Message, issues = Array.Empty<object>() });
    }

    public sealed record MassBookRequest(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);

    [HttpPost("book-all")]
    public IActionResult BookAllAsync([FromBody] MassBookRequest req)
    {
        var existing = _taskManager.GetAll()
            .FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued));
        if (existing != null)
        {
            return Accepted(new { running = true, processed = existing.Processed ?? 0, failed = 0, total = existing.Total ?? 0, warnings = existing.Warnings, errors = existing.Errors, message = existing.Message, issues = Array.Empty<object>() });
        }
        var payload = new { req.IgnoreWarnings, req.AbortOnFirstIssue, req.BookEntriesIndividually };
        var info = _taskManager.Enqueue(BackgroundTaskType.BookAllDrafts, _current.UserId, payload, allowDuplicate: false);
        _logger.LogInformation("Enqueued booking background task {TaskId} for user {UserId}", info.Id, _current.UserId);
        return Accepted(new { running = true, processed = 0, failed = 0, total = 0, warnings = 0, errors = 0, message = "Queued", issues = Array.Empty<object>() });
    }

    [HttpPost("book-all/cancel")]
    public IActionResult CancelBookAll()
    {
        var task = _taskManager.GetAll().FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BookAllDrafts && t.Status == BackgroundTaskStatus.Running);
        if (task == null) { return Accepted(); }
        _taskManager.TryCancel(task.Id);
        return Accepted();
    }

    [HttpGet("{draftId:guid}")]
    public async Task<IActionResult> GetAsync(Guid draftId, [FromQuery] bool headerOnly = false, [FromQuery] string? src = null, [FromQuery] Guid? fromEntryDraftId = null, [FromQuery] Guid? fromEntryId = null, CancellationToken ct = default)
    {
        StatementDraftDto? draft = headerOnly
            ? await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct)
            : await _drafts.GetDraftAsync(draftId, _current.UserId, ct);
        if (draft is null) { return NotFound(); }
        var neighbors = await _drafts.GetUploadGroupNeighborsAsync(draftId, _current.UserId, ct);
        return Ok(new
        {
            draft.DraftId,
            draft.OriginalFileName,
            draft.Description,
            draft.DetectedAccountId,
            draft.Status,
            draft.TotalAmount,
            draft.IsSplitDraft,
            draft.ParentDraftId,
            draft.ParentEntryId,
            draft.ParentEntryAmount,
            draft.UploadGroupId,
            Entries = draft.Entries,
            PrevInUpload = neighbors.prevId,
            NextInUpload = neighbors.nextId,
            Src = src,
            FromEntryDraftId = fromEntryDraftId,
            FromEntryId = fromEntryId
        });
    }

    [HttpGet("{draftId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> GetEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var draft = await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct);
        var ordered = (await _drafts.GetDraftEntriesAsync(draftId, ct)).OrderBy(e => e.BookingDate).ThenBy(e => e.Id).ToList();
        var entry = await _drafts.GetDraftEntryAsync(draftId, entryId, ct);
        if (entry is null) { return NotFound(); }
        var index = ordered.FindIndex(e => e.Id == entryId);
        var prev = index > 0 ? ordered[index - 1].Id : (Guid?)null;
        var next = index < ordered.Count - 1 ? ordered[index + 1].Id : (Guid?)null;
        var nextOpen = ordered.Skip(index + 1)
            .FirstOrDefault(e => e.Status == StatementDraftEntryStatus.Open || e.Status == StatementDraftEntryStatus.Announced)?.Id;

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

        Guid? bankContactId = null;
        if (draft!.DetectedAccountId.HasValue)
        {
            var accountService = HttpContext.RequestServices.GetRequiredService<IAccountService>();
            var account = await accountService.GetAsync(draft.DetectedAccountId.Value, _current.UserId, ct);
            bankContactId = account?.BankContactId;
        }

        return Ok(new
        {
            draft.DraftId,
            draft.OriginalFileName,
            Entry = entry,
            PrevEntryId = prev,
            NextEntryId = next,
            NextOpenEntryId = nextOpen,
            SplitSum = splitSum,
            Difference = diff,
            BankContactId = bankContactId
        });
    }

    [HttpPost("{draftId:guid}/entries")]
    public async Task<IActionResult> AddEntryAsync(Guid draftId, [FromBody] AddEntryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var draft = await _drafts.AddEntryAsync(draftId, _current.UserId, req.BookingDate, req.Amount, req.Subject, ct);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpPost("{draftId:guid}/classify")]
    public async Task<IActionResult> ClassifyAsync(Guid draftId, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.ClassifyAsync(draftId, null, _current.UserId, ct);
            return draft is null ? NotFound() : Ok(draft);
        }
        catch (Exception ex)
        {
            return BadRequest(ex);
        }
    }

    [HttpPost("{draftId:guid}/classify/{entryId:guid}")]
    public async Task<IActionResult> ClassifyEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.ClassifyAsync(draftId, entryId, _current.UserId, ct);
            return draft is null ? NotFound() : Ok(draft);
        }
        catch (Exception ex)
        {
            return BadRequest(ex);
        }
    }

    [HttpPost("{draftId:guid}/account/{accountId:guid}")]
    public async Task<IActionResult> SetAccountAsync(Guid draftId, Guid accountId, CancellationToken ct)
    {
        var draft = await _drafts.SetAccountAsync(draftId, _current.UserId, accountId, ct);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpPost("{draftId:guid}/commit")]
    public async Task<IActionResult> CommitAsync(Guid draftId, [FromBody] CommitRequest req, CancellationToken ct)
    {
        var result = await _drafts.CommitAsync(draftId, _current.UserId, req.AccountId, req.Format, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/contact")]
    public async Task<IActionResult> SetEntryContactAsync(Guid draftId, Guid entryId, [FromBody] SetContactRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryContactAsync(draftId, entryId, body.ContactId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/costneutral")]
    public async Task<IActionResult> SetEntryCostNeutralAsync(Guid draftId, Guid entryId, [FromBody] SetCostNeutralRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryCostNeutralAsync(draftId, entryId, body.IsCostNeutral, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/savingsplan")]
    public async Task<IActionResult> SetEntrySavingPlanAsync(Guid draftId, Guid entryId, [FromBody] SetSavingsPlanRequest body, CancellationToken ct)
    {
        var draft = await _drafts.AssignSavingsPlanAsync(draftId, entryId, body.SavingsPlanId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    public sealed record SetSplitDraftRequest(Guid? SplitDraftId);

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/split")]
    public async Task<IActionResult> SetEntrySplitDraftAsync(Guid draftId, Guid entryId, [FromBody] SetSplitDraftRequest body, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.SetEntrySplitDraftAsync(draftId, entryId, body.SplitDraftId, _current.UserId, ct);
            if (draft == null) { return NotFound(); }
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
            return Ok(new { Entry = entry, SplitSum = splitSum, Difference = diff });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{draftId:guid}")]
    public async Task<IActionResult> CancelAsync(Guid draftId, CancellationToken ct)
    {
        var ok = await _drafts.CancelAsync(draftId, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpGet("{draftId:guid}/file")]
    public async Task<IActionResult> DownloadOriginalAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await _drafts.GetDraftHeaderAsync(draftId, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var list = await _attachments.ListAsync(_current.UserId, AttachmentEntityKind.StatementDraft, draftId, 0, 1, ct);
        var fileMeta = list.FirstOrDefault();
        if (fileMeta == null) { return NotFound(); }
        var payload = await _attachments.DownloadAsync(_current.UserId, fileMeta.Id, ct);
        if (payload == null) { return NotFound(); }
        var (content, fileName, contentType) = payload.Value;
        return File(content, string.IsNullOrWhiteSpace(contentType) ? MediaTypeNames.Application.Octet : contentType, fileName);
    }

    public sealed record AddEntryRequest([Required] DateTime BookingDate, [Required] decimal Amount, [Required, MaxLength(500)] string Subject);
    public sealed record CommitRequest(Guid AccountId, ImportFormat Format);
    public sealed record SetContactRequest(Guid? ContactId);
    public sealed record SetCostNeutralRequest(bool? IsCostNeutral);
    public sealed record SetSavingsPlanRequest(Guid? SavingsPlanId);
    public sealed record SetArchiveSavingsPlanOnBookingRequest(bool ArchiveOnBooking);
    public sealed record UpdateEntryCoreRequest(DateTime BookingDate, DateTime? ValutaDate, decimal Amount, string Subject, string? RecipientName, string? CurrencyCode, string? BookingDescription);

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/edit-core")]
    public async Task<IActionResult> UpdateEntryCoreAsync(Guid draftId, Guid entryId, [FromBody] UpdateEntryCoreRequest body, CancellationToken ct)
    {
        var updated = await _drafts.UpdateEntryCoreAsync(draftId, entryId, _current.UserId, body.BookingDate, body.ValutaDate, body.Amount, body.Subject, body.RecipientName, body.CurrencyCode, body.BookingDescription, ct);
        return updated == null ? NotFound() : Ok(updated);
    }

    public sealed record SetEntrySecurityRequest(Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/security")
    ]
    public async Task<IActionResult> SetEntrySecurityAsync(Guid draftId, Guid entryId, [FromBody] SetEntrySecurityRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntrySecurityAsync(draftId, entryId, body.SecurityId, body.TransactionType, body.Quantity, body.FeeAmount, body.TaxAmount, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/savingsplan/archive-on-booking")]
    public async Task<IActionResult> SetEntryArchiveSavingsPlanOnBookingAsync(Guid draftId, Guid entryId, [FromBody] SetArchiveSavingsPlanOnBookingRequest body, CancellationToken ct)
    {
        var draft = await _drafts.SetEntryArchiveSavingsPlanOnBookingAsync(draftId, entryId, body.ArchiveOnBooking, _current.UserId, ct);
        if (draft == null) { return NotFound(); }
        var entry = draft.Entries.First(e => e.Id == entryId);
        return Ok(entry);
    }

    [HttpGet("{draftId:guid}/validate")]
    public async Task<IActionResult> ValidateAsync(Guid draftId, CancellationToken ct)
    {
        var result = await _drafts.ValidateAsync(draftId, null, _current.UserId, ct);
        return Ok(result);
    }

    [HttpGet("{draftId:guid}/entries/{entryId:guid}/validate")]
    public async Task<IActionResult> ValidateEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var result = await _drafts.ValidateAsync(draftId, entryId, _current.UserId, ct);
        return Ok(result);
    }

    [HttpPost("{draftId:guid}/book")]
    public async Task<IActionResult> BookAsync(Guid draftId, [FromQuery] bool forceWarnings = false, CancellationToken ct = default)
    {
        var result = await _drafts.BookAsync(draftId, null, _current.UserId, forceWarnings, ct);
        if (!result.Success && result.Validation.Messages.Any(m => m.Severity == "Error")) { return BadRequest(result); }
        if (!result.Success && result.HasWarnings) { return StatusCode(StatusCodes.Status428PreconditionRequired, result); }
        return Ok(result);
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/book")]
    public async Task<IActionResult> BookEntryAsync(Guid draftId, Guid entryId, [FromQuery] bool forceWarnings = false, CancellationToken ct = default)
    {
        var result = await _drafts.BookAsync(draftId, entryId, _current.UserId, forceWarnings, ct);
        if (!result.Success && result.Validation.Messages.Any(m => m.Severity == "Error")) { return BadRequest(result); }
        if (!result.Success && result.HasWarnings) { return StatusCode(StatusCodes.Status428PreconditionRequired, result); }
        return Ok(result);
    }

    public sealed record SaveEntryAllRequest(Guid? ContactId, bool? IsCostNeutral, Guid? SavingsPlanId, bool? ArchiveOnBooking, Guid? SecurityId, SecurityTransactionType? TransactionType, decimal? Quantity, decimal? FeeAmount, decimal? TaxAmount);

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/save-all")]
    public async Task<IActionResult> SaveEntryAllAsync(Guid draftId, Guid entryId, [FromBody] SaveEntryAllRequest body, CancellationToken ct)
    {
        var dto = await _drafts.SaveEntryAllAsync(draftId, entryId, _current.UserId, body.ContactId, body.IsCostNeutral, body.SavingsPlanId, body.ArchiveOnBooking, body.SecurityId, body.TransactionType, body.Quantity, body.FeeAmount, body.TaxAmount, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{draftId:guid}/entries/{entryId:guid}")]
    public async Task<IActionResult> DeleteEntryAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var ok = await _drafts.DeleteEntryAsync(draftId, entryId, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{draftId:guid}/entries/{entryId:guid}/reset-duplicate")]
    public async Task<IActionResult> ResetDuplicateAsync(Guid draftId, Guid entryId, CancellationToken ct)
    {
        var dto = await _drafts.ResetDuplicateEntryAsync(draftId, entryId, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }
}
