using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using FinanceManager.Web.Infrastructure.Attachments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using Microsoft.AspNetCore.DataProtection;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/attachments")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AttachmentsController : ControllerBase
{
    private readonly IAttachmentService _service;
    private readonly IAttachmentCategoryService _cats;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AttachmentsController> _logger;
    private readonly AttachmentUploadOptions _options;
    private readonly IStringLocalizer<AttachmentsController> _localizer;
    private readonly IDataProtector _protector;

    // API page size safety cap
    private const int MaxTake = 200;

    private const string ProtectorPurpose = "AttachmentDownloadToken";

    public AttachmentsController(
        IAttachmentService service,
        IAttachmentCategoryService cats,
        ICurrentUserService current,
        ILogger<AttachmentsController> logger,
        IOptions<AttachmentUploadOptions> options,
        IStringLocalizer<AttachmentsController> localizer,
        IDataProtectionProvider dp)
    {
        _service = service; _cats = cats; _current = current; _logger = logger; _options = options.Value; _localizer = localizer;
        _protector = dp.CreateProtector(ProtectorPurpose);
    }

    [HttpGet("{entityKind}/{entityId:guid}")]
    [ProducesResponseType(typeof(PageResult<AttachmentDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(short entityKind, Guid entityId, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] Guid? categoryId = null, [FromQuery] bool? isUrl = null, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = _localizer["Error_InvalidEntityKind"] }); }
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
    public async Task<IActionResult> UploadAsync(short entityKind, Guid entityId, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, [FromForm] string? url, CancellationToken ct = default, [FromQuery] AttachmentRole? role = null)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new { error = _localizer["Error_InvalidEntityKind"] }); }
        if (file == null && string.IsNullOrWhiteSpace(url)) { return BadRequest(new { error = _localizer["Error_FileOrUrlRequired"] }); }

        // Validation: either URL or file; if file then enforce size and mime
        if (file != null)
        {
            if (file.Length <= 0) { return BadRequest(new { error = _localizer["Error_EmptyFile"] }); }
            if (file.Length > _options.MaxSizeBytes)
            {
                // show limit in MB if cleanly divisible by MB, else bytes
                const long OneMb = 1024L * 1024L;
                string limitStr = (_options.MaxSizeBytes % OneMb == 0)
                    ? string.Format(System.Globalization.CultureInfo.CurrentUICulture, "{0} MB", _options.MaxSizeBytes / OneMb)
                    : string.Format(System.Globalization.CultureInfo.CurrentUICulture, "{0:N0} bytes", _options.MaxSizeBytes);
                return BadRequest(new { error = string.Format(System.Globalization.CultureInfo.CurrentUICulture, _localizer["Error_FileTooLarge"], limitStr) });
            }
            if (_options.AllowedMimeTypes?.Length > 0)
            {
                var ctIn = (file.ContentType ?? string.Empty).Trim();
                var ok = _options.AllowedMimeTypes.Any(m => string.Equals(m, ctIn, StringComparison.OrdinalIgnoreCase));
                if (!ok)
                {
                    return BadRequest(new { error = string.Format(System.Globalization.CultureInfo.CurrentUICulture, _localizer["Error_UnsupportedContentType"], ctIn) });
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
                // Determine category to use. For symbol uploads, ensure a system category 'Symbole' exists and assign it.
                Guid? useCategory = categoryId;

                if (role == AttachmentRole.Symbol)
                {
                    try
                    {
                        var cats = await _cats.ListAsync(_current.UserId, ct);
                        // Find system category by name 'Symbole' (case-insensitive)
                        var symbolCat = cats.FirstOrDefault(c => c.IsSystem && string.Equals(c.Name, "Symbole", StringComparison.OrdinalIgnoreCase));
                        if (symbolCat != null)
                        {
                            useCategory = symbolCat.Id;
                        }
                        else
                        {
                            var created = await _cats.CreateAsync(_current.UserId, "Symbole", isSystem: true, ct);
                            useCategory = created.Id;
                        }
                    }
                    catch
                    {
                        // ignore and proceed without category if creation fails
                        useCategory = categoryId;
                    }
                }

                using var stream = file!.OpenReadStream();
                if (role.HasValue)
                {
                    var dto = await _service.UploadAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, stream, file.FileName, file.ContentType ?? "application/octet-stream", useCategory, role.Value, ct);
                    return Ok(dto);
                }
                else
                {
                    var dto = await _service.UploadAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, stream, file.FileName, file.ContentType ?? "application/octet-stream", useCategory, ct);
                    return Ok(dto);
                }
            }
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload attachment failed for kind={Kind} entity={Entity}", entityKind, entityId);
            return Problem(_localizer["Error_UnexpectedError"], statusCode: 500);
        }
    }

    /// <summary>
    /// Create a short-lived download token for an attachment belonging to the current user.
    /// Caller must be authenticated and owner of the attachment.
    /// </summary>
    [HttpPost("{id:guid}/download-token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateDownloadTokenAsync(Guid id, [FromQuery] int validSeconds = 60)
    {
        // verify attachment exists and owner
        var list = await _service.ListAsync(_current.UserId, AttachmentEntityKind.Account, Guid.Empty, 0, 1, ct: CancellationToken.None);
        // We cannot use ListAsync for arbitrary id; use DownloadAsync to check existence? Instead rely on internal service via Count/List is not appropriate here.
        // We'll call DownloadAsync to attempt verify (but it returns stream). Instead create a light check via service.CountAsync across all kinds
        // Simpler: try to get download; ask service.CountAsync across all parents is not available. We'll check by calling DownloadAsync and then dispose.
        try
        {
            // Attempt to get the attachment via service.DownloadAsync to verify ownership.
            var payload = await _service.DownloadAsync(_current.UserId, id, CancellationToken.None);
            if (payload == null) { return NotFound(); }
            // Since DownloadAsync already checked owner by using _current.UserId, allow token creation
            var expires = DateTime.UtcNow.AddSeconds(Math.Clamp(validSeconds, 10, 3600));
            var plain = string.Join('|', id.ToString(), _current.UserId.ToString(), expires.Ticks.ToString());
            var token = _protector.Protect(plain);
            return Ok(new { token });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreateDownloadToken failed for attachment {AttachmentId}", id);
            return NotFound();
        }
    }

    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAsync(Guid id, [FromQuery] string? token, CancellationToken ct)
    {
        // If user is authenticated, allow normal path (attachment owner check happens in service.DownloadAsync)
        if (User?.Identity?.IsAuthenticated == true)
        {
            var payload = await _service.DownloadAsync(_current.UserId, id, ct);
            if (payload == null) { return NotFound(); }
            var (content, fileName, contentType) = payload.Value;
            return File(content, contentType, fileName);
        }

        // Otherwise, validate token
        if (string.IsNullOrWhiteSpace(token)) { return NotFound(); }
        try
        {
            var plain = _protector.Unprotect(token);
            var parts = plain.Split('|');
            if (parts.Length != 3) { return NotFound(); }
            var tokenAttachmentId = Guid.Parse(parts[0]);
            var ownerUserId = Guid.Parse(parts[1]);
            var ticks = long.Parse(parts[2]);
            var expires = new DateTime(ticks, DateTimeKind.Utc);
            if (tokenAttachmentId != id) { return NotFound(); }
            if (DateTime.UtcNow > expires) { return NotFound(); }
            // Now fetch attachment content by owner id. We need to bypass current user id check; call service.DownloadAsync with ownerUserId context.
            var payload = await _service.DownloadAsync(ownerUserId, id, ct);
            if (payload == null) { return NotFound(); }
            var (content, fileName, contentType) = payload.Value;
            return File(content, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid download token for attachment {AttachmentId}", id);
            return NotFound();
        }
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
