using FinanceManager.Application;
using FinanceManager.Application.Backups;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IBackupService _svc;
    private readonly ICurrentUserService _current;
    private readonly IBackgroundTaskManager _taskManager;

    public BackupsController(IBackupService svc, ICurrentUserService current, IBackgroundTaskManager taskManager)
    { _svc = svc; _current = current; _taskManager = taskManager; }

    /// <summary>
    /// Lists all backups created or uploaded by the current user (most recent first client-side).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _svc.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new backup snapshot for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAsync(CancellationToken ct)
        => Ok(await _svc.CreateAsync(_current.UserId, ct));

    /// <summary>
    /// Uploads an existing backup file (binary) to make it available for restore operations.
    /// </summary>
    /// <param name="file">Backup file to upload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("upload")]
    [RequestSizeLimit(1_024_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_024_000_000)]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file == null || file.Length == 0) { return BadRequest(new ApiErrorDto("No file selected.")); }
            await using var s = file.OpenReadStream();
            var dto = await _svc.UploadAsync(_current.UserId, s, file.FileName, ct);
            return Ok(dto);
        }
        catch (FileLoadException)
        {
            return BadRequest(new ApiErrorDto("A backup with that filename already exists."));
        }
    }

    /// <summary>
    /// Downloads a backup file by id (range processing enabled for large files).
    /// </summary>
    /// <param name="id">Backup id.</param>
    /// <param name="ct">Cancellation token.</param>
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
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.ApplyAsync(_current.UserId, id, (s1, i1, i2, i3, i4) => { }, ct);
        return ok ? NoContent() : NotFound();
    }

    private sealed record BackupRestorePayload(Guid BackupId);

    /// <summary>
    /// Enqueues a background restore task for a backup if none is currently running or queued.
    /// Returns current status if a task is already active.
    /// </summary>
    /// <param name="id">Backup id to restore.</param>
    [HttpPost("{id:guid}/apply/start")]
    [ProducesResponseType(typeof(FinanceManager.Shared.Dtos.Admin.BackupRestoreStatusDto), StatusCodes.Status200OK)]
    public IActionResult StartApplyAsync(Guid id)
    {
        var existing = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued))
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault();
        if (existing != null)
        {
            return Ok(MapStatus(existing));
        }
        var payload = new BackupRestorePayload(id);
        var info = _taskManager.Enqueue(BackgroundTaskType.BackupRestore, _current.UserId, payload, allowDuplicate: false);
        return Ok(MapStatus(info));
    }

    /// <summary>
    /// Gets status of current or last backup restore task for the user.
    /// </summary>
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
}
