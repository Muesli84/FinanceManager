using FinanceManager.Application;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers.Securities; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// CRUD endpoints for securities and related symbol uploads.
/// Delegates business logic to <see cref="ISecurityService"/> and <see cref="IAttachmentService"/>.
/// </summary>
[ApiController]
[Route("api/securities")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecuritiesController : ControllerBase
{
    private readonly ISecurityService _service;
    private readonly ICurrentUserService _current;
    private readonly IAttachmentService _attachments;

    /// <summary>
    /// Creates a new instance of <see cref="SecuritiesController"/>.
    /// </summary>
    public SecuritiesController(ISecurityService service, ICurrentUserService current, IAttachmentService attachments)
    {
        _service = service;
        _current = current;
        _attachments = attachments;
    }

    /// <summary>
    /// Request payload for creating or updating securities.
    /// </summary>
    public sealed class SecurityRequest
    {
        [Required, MinLength(2)] public string Name { get; set; } = string.Empty;
        [Required, MinLength(3)] public string Identifier { get; set; } = string.Empty;
        [Required, MinLength(3)] public string CurrencyCode { get; set; } = "EUR";
        public string? Description { get; set; }
        public string? AlphaVantageCode { get; set; }
        public Guid? CategoryId { get; set; }
    }

    /// <summary>
    /// Lists securities for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await _service.ListAsync(_current.UserId, onlyActive, ct));

    /// <summary>
    /// Returns count of securities for the current user.
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    /// <summary>
    /// Gets a security by id.
    /// </summary>
    [HttpGet("{id:guid}", Name = "GetSecurityAsync")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Creates a new security.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        // FIX: Use CreatedAtRoute because we referenced the named route (not the action method name) before.
        return CreatedAtRoute("GetSecurityAsync", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing security.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Archives the security.
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes a security.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Sets a symbol attachment for the security.
    /// </summary>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Clears the symbol attachment for the security.
    /// </summary>
    [HttpDelete("{id:guid}/symbol")]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Uploads a symbol file for the security and assigns it.
    /// </summary>
    [HttpPost("{id:guid}/symbol")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> UploadSymbolAsync(Guid id, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, CancellationToken ct)
    {
        if (file == null) { return BadRequest(new { error = "File required" }); }
        try
        {
            using var stream = file.OpenReadStream();
            var dto = await _attachments.UploadAsync(_current.UserId, AttachmentEntityKind.Security, id, stream, file.FileName, file.ContentType ?? "application/octet-stream", categoryId, AttachmentRole.Symbol, ct);
            // assign symbol
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, dto.Id, ct);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            // avoid leaking details
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}

