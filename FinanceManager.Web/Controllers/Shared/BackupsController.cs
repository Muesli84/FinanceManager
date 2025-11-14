using FinanceManager.Application;
using FinanceManager.Application.Backups;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Endpoints to manage backups for the current user (list, create, upload, download, restore).
/// The controller delegates actual operations to <see cref="IBackupService"/> and enqueues restore tasks via <see cref="IBackgroundTaskManager"/>.
/// </summary>
[ApiController]
[Route("api/setup/backups")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BackupsController : ControllerBase
{
    private readonly IBackupService _svc;
    private readonly ICurrentUserService _current;
    private readonly IBackgroundTaskManager _taskManager;

    /// <summary>
    /// Creates a new instance of <see cref="BackupsController"/>.
    /// </summary>
    /// <param name="svc">Backup service.</param>
    /// <param name="current">Current user service.</param>
    /// <param name="taskManager">Background task manager used for restore tasks.</param>
    public BackupsController(IBackupService svc, ICurrentUserService current, IBackgroundTaskManager taskManager)
    { _svc = svc; _current = current; _taskManager = taskManager; }

    /// <summary>
    /// Lists available backups for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="BackupDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _svc.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new backup for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with created <see cref="BackupDto"/>.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAsync(CancellationToken ct)
        => Ok(await _svc.CreateAsync(_current.UserId, ct));

    /// <summary>
    /// Uploads a backup file for the current user.
    /// </summary>
    /// <param name="file">Multipart backup file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with created <see cref="BackupDto"/> or 400 on validation/duplicate filename.</returns>
    [HttpPost("upload")]
    [RequestSizeLimit(1_024_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_024_000_000)]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file == null || file.Length == 0) { return BadRequest("Keine Datei ausgewählt."); }
            await using var s = file.OpenReadStream();
            var dto = await _svc.UploadAsync(_current.UserId, s, file.FileName, ct);
            return Ok(dto);
        }
        catch (FileLoadException)
        {
            return BadRequest("Ein Backup mit dem Dateinamen ist bereits vorhanden.");
        }
    }

    /// <summary>
    /// Downloads a backup file stream for the current user.
    /// </summary>
    /// <param name="id">Backup identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>File stream result or 404 if not found.</returns>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var entry = (await _svc.ListAsync(_current.UserId, ct)).FirstOrDefault(e => e.Id == id);
        var stream = await _svc.OpenDownloadAsync(_current.UserId, id, ct);
        if (stream == null) { return NotFound(); }
        return File(stream, MediaTypeNames.Application.Octet, fileDownloadName: entry?.FileName ?? "backup", enableRangeProcessing: true);
    }

    /// <summary>
    /// Applies a backup immediately (legacy immediate apply). Kept for compatibility.
    /// </summary>
    /// <param name="id">Backup identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success, NotFound when not found.</returns>
    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> ApplyAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.ApplyAsync(_current.UserId, id, (s1, i1, i2, i3, i4) => { }, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Starts a background restore task for a backup. If a restore is already queued or running, returns its status.
    /// </summary>
    /// <param name="id">Backup identifier.</param>
    /// <returns>200 OK with status payload.</returns>
    [HttpPost("{id:guid}/apply/start")]
    public IActionResult StartApplyAsync(Guid id)
    {
        // Check existing queued/running restore for user
        var existing = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore && (t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued))
            .OrderByDescending(t => t.EnqueuedUtc)
            .FirstOrDefault();
        if (existing != null)
        {
            return Ok(MapStatus(existing));
        }
        var payload = new { BackupId = id };
        var info = _taskManager.Enqueue(BackgroundTaskType.BackupRestore, _current.UserId, payload, allowDuplicate: false);
        return Ok(MapStatus(info));
    }

    /// <summary>
    /// Returns the status of the latest restore task for the current user.
    /// </summary>
    /// <returns>200 OK with status payload.</returns>
    [HttpGet("restore/status")]
    public IActionResult GetStatus()
    {
        var tasks = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore)
            .OrderByDescending(t => t.EnqueuedUtc)
            .ToList();
        var active = tasks.FirstOrDefault(t => t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued) ?? tasks.FirstOrDefault();
        if (active == null)
        {
            return Ok(new { running = false });
        }
        return Ok(MapStatus(active));
    }

    /// <summary>
    /// Cancels an active restore task if running.
    /// </summary>
    /// <returns>204 NoContent.</returns>
    [HttpPost("restore/cancel")]
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
    /// Deletes a backup belonging to the current user.
    /// </summary>
    /// <param name="id">Backup identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or NotFound when missing.</returns>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Maps <see cref="BackgroundTaskInfo"/> into a serializable status payload returned by API endpoints.
    /// </summary>
    private static object MapStatus(BackgroundTaskInfo info)
    {
        var running = info.Status == BackgroundTaskStatus.Running || info.Status == BackgroundTaskStatus.Queued;
        var error = info.Status == BackgroundTaskStatus.Failed ? (info.ErrorDetail ?? info.Message) : null;
        return new
        {
            running,
            processed = info.Processed ?? 0,
            total = info.Total ?? 0,
            message = info.Message,
            error,
            processed2 = info.Processed2 ?? 0,
            total2 = info.Total2 ?? 0,
            message2 = info.Message2
        };
    }
}

