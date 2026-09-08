using FinanceManager.Application;
using FinanceManager.Application.Backups;
using FinanceManager.Application.Common;
using FinanceManager.Application.Reports;
using FinanceManager.Shared.Dtos.Admin;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides endpoints to manage user database backups: list, create, upload, download,
/// apply (immediate or queued background restore), cancel and delete.
/// </summary>
[ApiController]
[Route("api/setup/backups")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BackupsController : ControllerBase
{
    private const string Origin = "API_Backup";

    private readonly IBackupService _svc;
    private readonly ICurrentUserService _current;
    private readonly IBackgroundTaskManager _taskManager;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance of the <see cref="BackupsController"/>.
    /// </summary>
    /// <param name="svc">Backup service used to list, create, upload, download and manage backups.</param>
    /// <param name="current">Service to access current user context.</param>
    /// <param name="taskManager">Background task manager used to enqueue restore tasks.</param>
    /// <param name="localizer">Localizer for generating localized error messages.</param>
    public BackupsController(
        IBackupService svc,
        ICurrentUserService current,
        IBackgroundTaskManager taskManager,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _taskManager = taskManager;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists all backups created or uploaded by the current user (most recent first client-side).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a read-only list of <see cref="BackupDto"/> instances.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _svc.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new backup snapshot for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the created <see cref="BackupDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAsync(CancellationToken ct)
        => Ok(await _svc.CreateAsync(_current.UserId, ct));

    /// <summary>
    /// Uploads an existing backup file (binary) to make it available for restore operations.
    /// </summary>
    /// <param name="file">Backup file to upload (multipart form file).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the created <see cref="BackupDto"/>, or 400 Bad Request when the file is invalid or duplicate.</returns>
    /// <exception cref="System.IO.FileLoadException">Thrown when a backup with the same filename already exists (mapped to 400).</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("upload")]
    [RequestSizeLimit(1_024_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_024_000_000)]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                const string code = "Err_Invalid_File";
                var entry = _localizer[$"{Origin}_{code}"];
                var message = entry.ResourceNotFound ? "No file selected." : entry.Value;
                return BadRequest(ApiErrorDto.Create(Origin, code, message));
            }

            await using var s = file.OpenReadStream();
            var dto = await _svc.UploadAsync(_current.UserId, s, file.FileName, ct);
            return Ok(dto);
        }
        catch (FileLoadException)
        {
            const string code = "Err_Conflict_DuplicateFileName";
            var entry = _localizer[$"{Origin}_{code}"];
            var message = entry.ResourceNotFound ? "A backup with that filename already exists." : entry.Value;
            return BadRequest(ApiErrorDto.Create(Origin, code, message));
        }
        catch (BackupValidationException ex)
        {
            return BadRequest(ApiErrorDto.Create(Origin, ex.Code, ex.Message));
        }
    }

    /// <summary>
    /// Downloads a backup file by id (range processing enabled for large files).
    /// </summary>
    /// <param name="id">Backup id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>File stream result with range support when the backup exists; 404 Not Found otherwise.</returns>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var entry = (await _svc.ListAsync(_current.UserId, ct)).FirstOrDefault(e => e.Id == id);
        var stream = await _svc.OpenDownloadAsync(_current.UserId, id, ct);
        if (stream == null) { return NotFound(); }
        return File(stream, MediaTypeNames.Application.Octet, fileDownloadName: entry?.FileName ?? "backup", enableRangeProcessing: true);
    }

    /// <summary>
    /// Immediately applies (restores) the specified backup. Legacy synchronous variant; may block longer.
    /// </summary>
    /// <param name="id">Backup id.</param>
    /// <param name="request">Restore request payload containing the confirmation text used to guard against accidental restores.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when restore succeeded; 404 Not Found when the backup does not exist.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyAsync(Guid id, [FromBody] BackupRestoreRequestDto? request, CancellationToken ct)
    {
        var result = await _svc.ApplyAsync(_current.UserId, id, request?.ConfirmationText, confirmationAlreadyValidated: false, (s1, i1, i2, i3, i4) => { }, ct);
        return MapApplyResult(result);
    }

    private sealed record BackupRestorePayload(Guid BackupId, bool Confirmed, string ExpectedFileName);

    /// <summary>
    /// Enqueues a background restore task for a backup if none is currently running or queued.
    /// Returns current status if a task is already active.
    /// </summary>
    /// <param name="id">Backup id to restore.</param>
    /// <param name="request">Restore request payload containing the confirmation text (and optionally the expected file name) used to guard against accidental restores.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a <see cref="FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto"/> describing the enqueued or current task status.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPost("{id:guid}/apply/start")]
    [ProducesResponseType(typeof(FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartApplyAsync(Guid id, [FromBody] BackupRestoreRequestDto? request, CancellationToken ct)
    {
        var backup = await _svc.GetAsync(_current.UserId, id, ct);
        if (backup == null)
        {
            return NotFound();
        }

        if (!string.Equals(request?.ConfirmationText, backup.FileName, StringComparison.Ordinal) ||
            (request?.ExpectedFileName is not null && !string.Equals(request.ExpectedFileName, backup.FileName, StringComparison.Ordinal)))
        {
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Backup_ConfirmationRequired", "Restore confirmation must match the backup file name."));
        }

        var existing = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued))
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault();
        if (existing != null)
        {
            return Conflict(ApiErrorDto.Create(Origin, "Err_Backup_RestoreActive", "A backup restore is already active."));
        }

        var payload = new BackupRestorePayload(id, Confirmed: true, backup.FileName);
        var info = _taskManager.Enqueue(BackgroundTaskType.BackupRestore, _current.UserId, payload, allowDuplicate: false);
        return Ok(MapStatus(info));
    }

    /// <summary>
    /// Gets status of current or last backup restore task for the user.
    /// </summary>
    /// <returns>200 OK with a <see cref="FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto"/> describing the task status (empty status when none available).</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("restore/status")]
    [ProducesResponseType(typeof(FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var tasks = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore)
            .OrderByDescending(t => t.EnqueuedUtc)
            .ToList();
        var active = tasks.FirstOrDefault(t => t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued) ?? tasks.FirstOrDefault();
        if (active == null)
        {
            return Ok(new FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto(false, 0, 0, null, null, 0, 0, null));
        }
        return Ok(MapStatus(active));
    }

    /// <summary>
    /// Cancels the currently running backup restore task if present.
    /// </summary>
    /// <returns>204 No Content always.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpPost("restore/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Cancel()
    {
        var running = _taskManager.GetAll().FirstOrDefault(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore && t.Status == BackgroundTaskStatus.Running);
        if (running != null)
        {
            _taskManager.TryCancel(running.Id);
        }
        return NoContent();
    }

    /// <summary>
    /// Deletes a backup owned by the current user.
    /// </summary>
    /// <param name="id">Backup id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when deletion succeeded; 404 Not Found when the backup does not exist.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    private static FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto MapStatus(BackgroundTaskInfo info)
    {
        var running = info.Status == BackgroundTaskStatus.Running || info.Status == BackgroundTaskStatus.Queued;
        var error = info.Status == BackgroundTaskStatus.Failed ? (info.ErrorDetail ?? info.Message) : null;
        return new FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto(
            running,
            info.Processed ?? 0,
            info.Total ?? 0,
            info.Message,
            error,
            info.Processed2 ?? 0,
            info.Total2 ?? 0,
            info.Message2
        );
    }

    private IActionResult MapApplyResult(BackupApplyResult result)
    {
        return result.Status switch
        {
            BackupApplyStatus.Succeeded => NoContent(),
            BackupApplyStatus.NotFound => NotFound(),
            BackupApplyStatus.ConfirmationRequired => BadRequest(ApiErrorDto.Create(Origin, "Err_Backup_ConfirmationRequired", result.Message)),
            BackupApplyStatus.InvalidBackup => BadRequest(ApiErrorDto.Create(Origin, "Err_Backup_Invalid", result.Message)),
            BackupApplyStatus.ImportFailed => BadRequest(ApiErrorDto.Create(Origin, "Err_Backup_ImportFailed", result.Message)),
            _ => BadRequest(ApiErrorDto.Create(Origin, "Err_Backup_ApplyFailed", result.Message))
        };
    }
}
