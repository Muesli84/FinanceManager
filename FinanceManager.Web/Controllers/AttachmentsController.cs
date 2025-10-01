using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/attachments")] 
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class AttachmentsController : ControllerBase
{
    private readonly IAttachmentService _service;
    private readonly IAttachmentCategoryService _cats;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AttachmentsController> _logger;

    public AttachmentsController(IAttachmentService service, IAttachmentCategoryService cats, ICurrentUserService current, ILogger<AttachmentsController> logger)
    {
        _service = service; _cats = cats; _current = current; _logger = logger;
    }

    [HttpGet("{entityKind}/{entityId:guid}")] 
    [ProducesResponseType(typeof(IReadOnlyList<AttachmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(short entityKind, Guid entityId, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = "Invalid entityKind" }); }
        var list = await _service.ListAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, skip, take, ct);
        return Ok(list);
    }

    [HttpPost("{entityKind}/{entityId:guid}")] 
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadAsync(short entityKind, Guid entityId, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, [FromForm] string? url, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = "Invalid entityKind" }); }
        if (file == null && string.IsNullOrWhiteSpace(url)) { return BadRequest(new { error = "file or url required" }); }
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                var dto = await _service.CreateUrlAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, url!, null, categoryId, ct);
                return Ok(dto);
            }
            else
            {
                using var stream = file!.OpenReadStream();
                var dto = await _service.UploadAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, stream, file.FileName, file.ContentType, categoryId, ct);
                return Ok(dto);
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload attachment failed for kind={Kind} entity={Entity}", entityKind, entityId);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpGet("{id:guid}/download")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAsync(Guid id, CancellationToken ct)
    {
        var payload = await _service.DownloadAsync(_current.UserId, id, ct);
        if (payload == null) { return NotFound(); }
        var (content, fileName, contentType) = payload.Value;
        return File(content, contentType, fileName);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    public sealed record UpdateCategoryRequest(Guid? CategoryId);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCategoryAsync(Guid id, [FromBody] UpdateCategoryRequest req, CancellationToken ct)
    {
        var ok = await _service.UpdateCategoryAsync(_current.UserId, id, req.CategoryId, ct);
        return ok ? NoContent() : NotFound();
    }

    // Categories ------------------------------

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<AttachmentCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategoriesAsync(CancellationToken ct)
        => Ok(await _cats.ListAsync(_current.UserId, ct));

    public sealed record CreateCategoryRequest([Required, MinLength(2)] string Name);

    [HttpPost("categories")]
    [ProducesResponseType(typeof(AttachmentCategoryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategoryAsync([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _cats.CreateAsync(_current.UserId, req.Name.Trim(), ct);
            return CreatedAtAction(nameof(ListCategoriesAsync), new { }, dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _cats.DeleteAsync(_current.UserId, id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}
