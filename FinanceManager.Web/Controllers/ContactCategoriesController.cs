using FinanceManager.Application;
using FinanceManager.Application.Contacts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages contact categories for the current user: CRUD operations and symbol assignment.
/// </summary>
[ApiController]
[Route("api/contact-categories")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactCategoriesController : ControllerBase
{
    private readonly IContactCategoryService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ContactCategoriesController> _logger;

    public ContactCategoriesController(IContactCategoryService svc, ICurrentUserService current, ILogger<ContactCategoriesController> logger)
    { _svc = svc; _current = current; _logger = logger; }

    /// <summary>
    /// Lists all contact categories owned by the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ContactCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try { var list = await _svc.ListAsync(_current.UserId, ct); return Ok(list); }
        catch (Exception ex) { _logger.LogError(ex, "List categories failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Creates a new contact category.
    /// </summary>
    /// <param name="req">Creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] ContactCategoryCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var created = await _svc.CreateAsync(_current.UserId, req.Name, ct); return Created($"/api/contact-categories/{created.Id}", created); }
        catch (ArgumentException ex) { return BadRequest(new ApiErrorDto(ex.Message)); }
        catch (Exception ex) { _logger.LogError(ex, "Create category failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Gets a single contact category by id.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await _svc.GetAsync(id, _current.UserId, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (Exception ex) { _logger.LogError(ex, "Get category failed {CategoryId}", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Updates the name of a contact category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] ContactCategoryUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { await _svc.UpdateAsync(id, _current.UserId, req.Name, ct); return NoContent(); }
        catch (ArgumentException ex) { _logger.LogWarning(ex, "Update failed for contact category {CategoryId}", id); return NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "Update failed for contact category {CategoryId}", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Deletes a category owned by the current user.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try { await _svc.DeleteAsync(id, _current.UserId, ct); return NoContent(); }
        catch (ArgumentException ex) { _logger.LogWarning(ex, "Delete failed for contact category {CategoryId}", id); return NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "Delete failed for contact category {CategoryId}", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Assigns a symbol attachment to the category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try { await _svc.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct); return NoContent(); }
        catch (ArgumentException ex) { _logger.LogWarning(ex, "SetSymbol failed for contact category {CategoryId}", id); return NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "SetSymbol failed for contact category {CategoryId}", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Clears any symbol attachment from the category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try { await _svc.SetSymbolAttachmentAsync(id, _current.UserId, null, ct); return NoContent(); }
        catch (ArgumentException ex) { _logger.LogWarning(ex, "ClearSymbol failed for contact category {CategoryId}", id); return NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "ClearSymbol failed for contact category {CategoryId}", id); return Problem("Unexpected error", statusCode: 500); }
    }
}