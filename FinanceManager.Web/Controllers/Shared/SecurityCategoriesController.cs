using FinanceManager.Application;
using FinanceManager.Application.Securities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Manages security categories for the current user.
/// </summary>
[ApiController]
[Route("api/security-categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityCategoriesController : ControllerBase
{
    private readonly ISecurityCategoryService _service;
    private readonly ICurrentUserService _current;

    /// <summary>
    /// Creates a new instance of <see cref="SecurityCategoriesController"/>.
    /// </summary>
    public SecurityCategoriesController(ISecurityCategoryService service, ICurrentUserService current)
    {
        _service = service; _current = current;
    }

    /// <summary>
    /// Payload for creating/updating a category.
    /// </summary>
    public sealed class CategoryRequest
    {
        [Required, MinLength(2)]
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Gets a security category by id.
    /// </summary>
    [HttpGet("{id:guid}", Name = "GetSecurityCategory")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Lists security categories for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _service.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new security category.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, ct);

        // Verwendet jetzt den eindeutig benannten Route-Namen statt CreatedAtAction (vermeidet „No route matches…“)
        return CreatedAtRoute("GetSecurityCategory", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing security category.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] CategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Deletes a security category.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Sets the symbol attachment for the category.
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
    /// Clears the symbol attachment for the category.
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
}
