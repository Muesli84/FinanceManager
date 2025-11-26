using FinanceManager.Application;
using FinanceManager.Application.Backups;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

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

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BackupDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _svc.ListAsync(_current.UserId, ct));

    [HttpPost]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateAsync(CancellationToken ct)
        => Ok(await _svc.CreateAsync(_current.UserId, ct));

    [HttpPost("upload")]
    [RequestSizeLimit(1_024_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 1_024_000_000)]
    [ProducesResponseType(typeof(BackupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file == null || file.Length == 0) { return BadRequest(new ApiErrorDto("Keine Datei ausgewählt.")); }
            await using var s = file.OpenReadStream();
            var dto = await _svc.UploadAsync(_current.UserId, s, file.FileName, ct);
            return Ok(dto);
        }
        catch (FileLoadException)
        {
            return BadRequest(new ApiErrorDto("Ein Backup mit dem Dateinamen ist bereits vorhanden."));
        }
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var entry = (await _svc.ListAsync(_current.UserId, ct)).FirstOrDefault(e => e.Id == id);
        var stream = await _svc.OpenDownloadAsync(_current.UserId, id, ct);
        if (stream == null) { return NotFound(); }
        return File(stream, MediaTypeNames.Application.Octet, fileDownloadName: entry?.FileName ?? "backup", enableRangeProcessing: true);
    }

    // Legacy immediate apply (kept for compatibility)
    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApplyAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.ApplyAsync(_current.UserId, id, (s1, i1, i2, i3, i4) => { }, ct);
        return ok ? NoContent() : NotFound();
    }

    private sealed record BackupRestorePayload(Guid BackupId);

    // Background restore via generic background task queue
    [HttpPost("{id:guid}/apply/start")]
    [ProducesResponseType(typeof(BackupRestoreStatusDto), StatusCodes.Status200OK)]
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
        var payload = new BackupRestorePayload(id);
        var info = _taskManager.Enqueue(BackgroundTaskType.BackupRestore, _current.UserId, payload, allowDuplicate: false);
        return Ok(MapStatus(info));
    }

    [HttpGet("restore/status")]
    [ProducesResponseType(typeof(BackupRestoreStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var tasks = _taskManager.GetAll()
            .Where(t => t.UserId == _current.UserId && t.Type == BackgroundTaskType.BackupRestore)
            .OrderByDescending(t => t.EnqueuedUtc)
            .ToList();
        var active = tasks.FirstOrDefault(t => t.Status == BackgroundTaskStatus.Running || t.Status == BackgroundTaskStatus.Queued) ?? tasks.FirstOrDefault();
        if (active == null)
        {
            return Ok(new BackupRestoreStatusDto(false, 0, 0, null, null, 0, 0, null));
        }
        return Ok(MapStatus(active));
    }

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

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    private static BackupRestoreStatusDto MapStatus(BackgroundTaskInfo info)
    {
        var running = info.Status == BackgroundTaskStatus.Running || info.Status == BackgroundTaskStatus.Queued;
        var error = info.Status == BackgroundTaskStatus.Failed ? (info.ErrorDetail ?? info.Message) : null;
        return new BackupRestoreStatusDto(
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
