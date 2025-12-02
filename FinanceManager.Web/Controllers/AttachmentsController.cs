using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Web.Infrastructure.Attachments;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages file and URL attachments plus their categories for the authenticated user.
/// Supports upload, listing (paged), download (with optional short-lived token), update and deletion.
/// </summary>
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

    /// <summary>
    /// Returns a paged list of attachments for an entity (optional filtering by category, URL/file, search term).
    /// </summary>
    /// <param name="entityKind">Attachment entity kind enum value.</param>
    /// <param name="entityId">Entity id.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Max items to return (capped).</param>
    /// <param name="categoryId">Optional category filter.</param>
    /// <param name="isUrl">True to filter URL attachments only, false for files only.</param>
    /// <param name="q">Optional search term (file name / url substring).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{entityKind}/{entityId:guid}")]
    [ProducesResponseType(typeof(PageResult<AttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(short entityKind, Guid entityId, [FromQuery] int skip = 0, [FromQuery] int take = 50, [FromQuery] Guid? categoryId = null, [FromQuery] bool? isUrl = null, [FromQuery] string? q = null, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new ApiErrorDto(_localizer["Error_InvalidEntityKind"])); }
        if (skip < 0) { skip = 0; }
        if (take <= 0) { take = 50; }
        if (take > MaxTake) { take = MaxTake; }

        var items = await _service.ListAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, skip, take, categoryId, isUrl, q, ct);
        var total = await _service.CountAsync(_current.UserId, (AttachmentEntityKind)entityKind, entityId, categoryId, isUrl, q, ct);
        var hasMore = skip + items.Count < total;
        return Ok(new PageResult<AttachmentDto> { Items = items.ToList(), HasMore = hasMore, Total = total });
    }

    /// <summary>
    /// Uploads a file or creates a URL attachment. Optionally sets category and role (e.g. Symbol).
    /// </summary>
    /// <param name="entityKind">Target entity kind.</param>
    /// <param name="entityId">Target entity id.</param>
    /// <param name="file">File content (if not a URL attachment).</param>
    /// <param name="categoryId">Optional category id.</param>
    /// <param name="url">External URL (if not a file upload).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="role">Attachment role to assign (optional).</param>
    [HttpPost("{entityKind}/{entityId:guid}")]
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadAsync(short entityKind, Guid entityId, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, [FromForm] string? url, CancellationToken ct = default, [FromQuery] AttachmentRole? role = null)
    {
        if (!Enum.IsDefined(typeof(AttachmentEntityKind), entityKind)) { return BadRequest(new ApiErrorDto(_localizer["Error_InvalidEntityKind"])); }
        if (file == null && string.IsNullOrWhiteSpace(url)) { return BadRequest(new ApiErrorDto(_localizer["Error_FileOrUrlRequired"])); }

        // Validation: either URL or file; if file then enforce size and mime
        if (file != null)
        {
            if (file.Length <= 0) { return BadRequest(new ApiErrorDto(_localizer["Error_EmptyFile"])); }
            if (file.Length > _options.MaxSizeBytes)
            {
                // show limit in MB if cleanly divisible by MB, else bytes
                const long OneMb = 1024L * 1024L;
                string limitStr = (_options.MaxSizeBytes % OneMb == 0)
                    ? string.Format(System.Globalization.CultureInfo.CurrentUICulture, "{0} MB", _options.MaxSizeBytes / OneMb)
                    : string.Format(System.Globalization.CultureInfo.CurrentUICulture, "{0:N0} bytes", _options.MaxSizeBytes);
                return BadRequest(new ApiErrorDto(string.Format(System.Globalization.CultureInfo.CurrentUICulture, _localizer["Error_FileTooLarge"], limitStr)));
            }
            if (_options.AllowedMimeTypes?.Length > 0)
            {
                var ctIn = (file.ContentType ?? string.Empty).Trim();
                var ok = _options.AllowedMimeTypes.Any(m => string.Equals(m, ctIn, StringComparison.OrdinalIgnoreCase));
                if (!ok)
                {
                    return BadRequest(new ApiErrorDto(string.Format(System.Globalization.CultureInfo.CurrentUICulture, _localizer["Error_UnsupportedContentType"], ctIn)));
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
            return BadRequest(new ApiErrorDto(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload attachment failed for kind={Kind} entity={Entity}", entityKind, entityId);
            return Problem(_localizer["Error_UnexpectedError"], statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a short-lived download token for an attachment owned by the current user.
    /// </summary>
    /// <param name="id">Attachment id.</param>
    /// <param name="validSeconds">Validity in seconds (10..3600).</param>
    [HttpPost("{id:guid}/download-token")]
    [ProducesResponseType(typeof(AttachmentDownloadTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateDownloadTokenAsync(Guid id, [FromQuery] int validSeconds = 60)
    {
        try
        {
            var payload = await _service.DownloadAsync(_current.UserId, id, CancellationToken.None);
            if (payload == null) { return NotFound(); }
            var expires = DateTime.UtcNow.AddSeconds(Math.Clamp(validSeconds, 10, 3600));
            var plain = string.Join('|', id.ToString(), _current.UserId.ToString(), expires.Ticks.ToString());
            var token = _protector.Protect(plain);
            return Ok(new AttachmentDownloadTokenDto(token));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreateDownloadToken failed for attachment {AttachmentId}", id);
            return NotFound();
        }
    }

    /// <summary>
    /// Downloads an attachment. If authenticated: direct access. If anonymous: token validation required.
    /// </summary>
    /// <param name="id">Attachment id.</param>
    /// <param name="token">Download token for anonymous access.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadAsync(Guid id, [FromQuery] string? token, CancellationToken ct)
    {
        // Prefer authenticated current user indicator over HttpContext principal to support unit tests
        if (_current.IsAuthenticated)
        {
            var payload = await _service.DownloadAsync(_current.UserId, id, ct);
            if (payload == null) { return NotFound(); }
            var (content, fileName, contentType) = payload.Value;
            return File(content, string.IsNullOrWhiteSpace(contentType) ? MediaTypeNames.Application.Octet : contentType, fileName);
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
            if (tokenAttachmentId != id || DateTime.UtcNow > expires) { return NotFound(); }
            var payload = await _service.DownloadAsync(ownerUserId, id, ct);
            if (payload == null) { return NotFound(); }
            var (content, fileName, contentType) = payload.Value;
            return File(content, string.IsNullOrWhiteSpace(contentType) ? MediaTypeNames.Application.Octet : contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid download token for attachment {AttachmentId}", id);
            return NotFound();
        }
    }

    /// <summary>
    /// Deletes an attachment owned by the current user.
    /// </summary>
    /// <param name="id">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(_current.UserId, id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Updates file name and category of an attachment.
    /// </summary>
    /// <param name="id">Attachment id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] AttachmentUpdateCoreRequest req, CancellationToken ct)
    {
        var ok = await _service.UpdateCoreAsync(_current.UserId, id, req.FileName, req.CategoryId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Sets only the category of an attachment.
    /// </summary>
    /// <param name="id">Attachment id.</param>
    /// <param name="req">Category update request.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}/category")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCategoryAsync(Guid id, [FromBody] AttachmentUpdateCategoryRequest req, CancellationToken ct)
    {
        var ok = await _service.UpdateCategoryAsync(_current.UserId, id, req.CategoryId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Lists all categories of the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<AttachmentCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategoriesAsync(CancellationToken ct)
        => Ok(await _cats.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new attachment category.
    /// </summary>
    /// <param name="req">Category creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("categories")]
    [ProducesResponseType(typeof(AttachmentCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCategoryAsync([FromBody] AttachmentCreateCategoryRequest req, CancellationToken ct)
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
            return BadRequest(new ApiErrorDto(ex.Message));
        }
    }

    /// <summary>
    /// Updates the name of an attachment category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="req">Update payload containing new name.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("categories/{id:guid}")]
    [ProducesResponseType(typeof(AttachmentCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategoryNameAsync(Guid id, [FromBody] AttachmentUpdateCategoryNameRequest req, CancellationToken ct)
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
            return Conflict(new ApiErrorDto(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiErrorDto(ex.Message));
        }
    }

    /// <summary>
    /// Deletes a category if allowed (e.g. not system protected).
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("categories/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategoryAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _cats.DeleteAsync(_current.UserId, id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiErrorDto(ex.Message));
        }
    }
}
