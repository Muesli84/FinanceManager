using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using FinanceManager.Web.Infrastructure.Attachments;

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
    private readonly AttachmentUploadOptions _options;

    // API page size safety cap
    private const int MaxTake = 200;

    public AttachmentsController(IAttachmentService service, IAttachmentCategoryService cats, ICurrentUserService current, ILogger<AttachmentsController> logger, IOptions<AttachmentUploadOptions> options)
    {
        _service = service; _cats = cats; _current = current; _logger = logger; _options = options.Value;
    }

    [HttpGet("{entityKind}/{entityId:guid}")] 
    [ProducesResponseType(typeof(PageResult<AttachmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(short entityKind, Guid entityId, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] Guid? categoryId = null, [FromQuery] bool? isUrl = null, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = "Invalid entityKind" }); }
        if (skip < 0) { skip = 0; }
        if (take <= 0) { take = 50; }
        if (take > MaxTake) { take = MaxTake; }

        var items = await _service.ListAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, skip, take, categoryId, isUrl, q, ct);
        var total = await _service.CountAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, categoryId, isUrl, q, ct);
        var hasMore = skip + items.Count < total;
        return Ok(new PageResult<AttachmentDto> { Items = items.ToList(), HasMore = hasMore, Total = total });
    }

    [HttpPost("{entityKind}/{entityId:guid}")] 
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadAsync(short entityKind, Guid entityId, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, [FromForm] string? url, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = "Invalid entityKind" }); }
        if (file == null && string.IsNullOrWhiteSpace(url)) { return BadRequest(new { error = "file or url required" }); }

        // Validation: either URL or file; if file then enforce size and mime
        if (file != null)
        {
            if (file.Length <= 0) { return BadRequest(new { error = "Empty file" }); }
            if (file.Length > _options.MaxSizeBytes)
            {
                return BadRequest(new { error = $"File too large. Max {_options.MaxSizeBytes} bytes." });
            }
            if (_options.AllowedMimeTypes?.Length > 0)
            {
                var ctIn = (file.ContentType ?? string.Empty).Trim();
                var ok = _options.AllowedMimeTypes.Any(m => string.Equals(m, ctIn, StringComparison.OrdinalIgnoreCase));
                if (!ok)
                {
                    return BadRequest(new { error = $"Unsupported content type '{ctIn}'." });
                }
            }
        }

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
    public sealed record UpdateCoreRequest(string? FileName, Guid? CategoryId);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateCoreRequest req, CancellationToken ct)
    {
        var ok = await _service.UpdateCoreAsync(_current.UserId, id, req.FileName, req.CategoryId, ct);
        return ok ? NoContent() : NotFound();
    }

    // Backward-compatible route for category only (if used elsewhere)
    [HttpPut("{id:guid}/category")]
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
    public sealed record UpdateCategoryNameRequest([Required, MinLength(2)] string Name);

    [HttpPost("categories")]
    [ProducesResponseType(typeof(AttachmentCategoryDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCategoryAsync([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _cats.CreateAsync(_current.UserId, req.Name.Trim(), ct);
            // Avoid CreatedAtAction routing issues; return Created with list endpoint as location
            return Created($"/api/attachments/categories", dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(typeof(AttachmentCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategoryNameAsync(Guid id, [FromBody] UpdateCategoryNameRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _cats.UpdateAsync(_current.UserId, id, req.Name.Trim(), ct);
            if (dto is null) return NotFound();
            return Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
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
