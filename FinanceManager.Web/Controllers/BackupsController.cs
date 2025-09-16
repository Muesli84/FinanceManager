using FinanceManager.Application.Backups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/setup/backups")]
[Authorize]
public sealed class BackupsController : ControllerBase
{
    private readonly IBackupService _svc;
    private readonly FinanceManager.Application.ICurrentUserService _current;
    private readonly Services.IBackupRestoreCoordinator _restore;

    public BackupsController(IBackupService svc, FinanceManager.Application.ICurrentUserService current, Services.IBackupRestoreCoordinator restore)
    { _svc = svc; _current = current; _restore = restore; }

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
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        try
        {
            if (file == null || file.Length == 0) return BadRequest("Keine Datei ausgewählt.");
            await using var s = file.OpenReadStream();
            var dto = await _svc.UploadAsync(_current.UserId, s, file.FileName, ct);
            return Ok(dto);
        }
        catch(FileLoadException)
        {
            return BadRequest("Ein Backup mit dem Dateinamen ist ebereits vorhanden.");
        }
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var entry = (await _svc.ListAsync(_current.UserId, ct)).FirstOrDefault(e => e.Id == id);
        var stream = await _svc.OpenDownloadAsync(_current.UserId, id, ct);
        if (stream == null) return NotFound();
        return File(stream, MediaTypeNames.Application.Octet, fileDownloadName: entry?.FileName ?? "backup", enableRangeProcessing: true);
    }

    // Legacy immediate apply (kept for compatibility)
    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> ApplyAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.ApplyAsync(_current.UserId, id, (i1, i2) => { }, ct);
        return ok ? NoContent() : NotFound();
    }

    // Background restore endpoints
    [HttpPost("{id:guid}/apply/start")]
    public async Task<IActionResult> StartApplyAsync(Guid id, CancellationToken ct)
    {
        var status = await _restore.StartAsync(_current.UserId, id, TimeSpan.FromMilliseconds(250), ct);
        return Ok(status);
    }

    [HttpGet("restore/status")]
    public IActionResult GetStatus()
    {
        var s = _restore.GetStatus(_current.UserId);
        return s == null ? Ok(new { running = false }) : Ok(s);
    }

    [HttpPost("restore/cancel")]
    public IActionResult Cancel()
    {
        _restore.Cancel(_current.UserId);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _svc.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }
}
