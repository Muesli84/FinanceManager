using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using FinanceManager.Application;
using FinanceManager.Application.Statements;
using FinanceManager.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    public StatementDraftsController(IStatementDraftService drafts, ICurrentUserService current, ILogger<StatementDraftsController> logger)
    { _drafts = drafts; _current = current; _logger = logger; }

    public sealed record UploadRequest([Required] string FileName);

    [HttpGet]
    public async Task<IActionResult> GetOpenAsync(CancellationToken ct)
    {
        var drafts = await _drafts.GetOpenDraftsAsync(_current.UserId, ct);
        return Ok(drafts);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UploadAsync([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "File required" });
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var draft = await _drafts.CreateDraftAsync(_current.UserId, file.FileName, ms.ToArray(), ct);
        return Ok(draft);
    }

    [HttpGet("{draftId:guid}")]
    public async Task<IActionResult> GetAsync(Guid draftId, CancellationToken ct)
    {
        var draft = await _drafts.GetDraftAsync(draftId, _current.UserId, ct);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpPost("{draftId:guid}/entries")]
    public async Task<IActionResult> AddEntryAsync(Guid draftId, [FromBody] AddEntryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var draft = await _drafts.AddEntryAsync(draftId, _current.UserId, req.BookingDate, req.Amount, req.Subject, ct);
        return draft is null ? NotFound() : Ok(draft);
    }

    [HttpPost("{draftId:guid}/commit")]
    public async Task<IActionResult> CommitAsync(Guid draftId, [FromBody] CommitRequest req, CancellationToken ct)
    {
        var result = await _drafts.CommitAsync(draftId, _current.UserId, req.AccountId, req.Format, ct);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpDelete("{draftId:guid}")]
    public async Task<IActionResult> CancelAsync(Guid draftId, CancellationToken ct)
    {
        var ok = await _drafts.CancelAsync(draftId, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    public sealed record AddEntryRequest([property:Required] DateTime BookingDate, [property:Required] decimal Amount, [property:Required, MaxLength(500)] string Subject);
    public sealed record CommitRequest(Guid AccountId, ImportFormat Format);
}
