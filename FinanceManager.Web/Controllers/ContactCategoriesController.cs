using FinanceManager.Application;
using FinanceManager.Application.Contacts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages contact categories for the current user (list, create, read, update, delete and symbol attachments).
/// Delegates business logic to <see cref="IContactCategoryService"/>.
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

    /// <summary>
    /// Creates a new instance of <see cref="ContactCategoriesController"/>.
    /// </summary>
    /// <param name="svc">Service for contact category operations.</param>
    /// <param name="current">Current user context.</param>
    /// <param name="logger">Logger instance.</param>
    public ContactCategoriesController(IContactCategoryService svc, ICurrentUserService current, ILogger<ContactCategoriesController> logger)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
    }

    /// <summary>
    /// Request payload to create a contact category.
    /// </summary>
    public sealed record CreateCategoryRequest([Required, MinLength(2)] string Name);

    /// <summary>
    /// Request payload to update a contact category.
    /// </summary>
    public sealed record UpdateCategoryRequest([Required, MinLength(2)] string Name);

    /// <summary>
    /// Lists contact categories for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="ContactCategoryDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ContactCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListAsync(_current.UserId, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List categories failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new contact category for the current user.
    /// </summary>
    /// <param name="req">Create request containing the category name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created <see cref="ContactCategoryDto"/>, or 400 on validation error.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.Name, ct);
            return Created($"/api/contact-categories/{created.Id}", created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create category failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Gets a contact category by id for the current user.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="ContactCategoryDto"/> or 404 when not found.</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await _svc.GetAsync(id, _current.UserId, ct);
            if (dto == null) return NotFound();
            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get category failed {CategoryId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates the name of a contact category owned by the current user.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="req">Update request containing the new name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success or 404 when not found.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            await _svc.UpdateAsync(id, _current.UserId, req.Name, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Update failed for contact category {CategoryId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for contact category {CategoryId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a contact category owned by the current user.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success or 404 when not found.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _svc.DeleteAsync(id, _current.UserId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Delete failed for contact category {CategoryId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed for contact category {CategoryId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Sets a symbol attachment for the contact category.
    /// </summary>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _svc.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SetSymbol failed for contact category {CategoryId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetSymbol failed for contact category {CategoryId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Clears the symbol attachment for the contact category.
    /// </summary>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _svc.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "ClearSymbol failed for contact category {CategoryId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for contact category {CategoryId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}