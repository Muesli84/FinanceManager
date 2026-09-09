using FinanceManager.Application;
using FinanceManager.Application.Common;
using FinanceManager.Application.Contacts;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
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
    private const string Origin = "API_ContactCategory";

    private readonly IContactCategoryService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ContactCategoriesController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance of <see cref="ContactCategoriesController"/>.
    /// </summary>
    /// <param name="svc">Service for managing contact categories.</param>
    /// <param name="current">Service providing information about the current user context.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">Localizer for string resources.</param>
    public ContactCategoriesController(
        IContactCategoryService svc,
        ICurrentUserService current,
        ILogger<ContactCategoriesController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists all contact categories owned by the current user.
    /// </summary>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 200 with a read-only list of <see cref="ContactCategoryDto"/> on success; HTTP 500 on unexpected error.</returns>
    /// <exception cref="Exception">Thrown when an unexpected server error occurs while listing categories.</exception>
    /// <response code="200">The HTTP 200 response.</response>
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
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates a new contact category for the current user.
    /// </summary>
    /// <param name="req">Creation payload containing the category name.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// HTTP 201 with the created <see cref="ContactCategoryDto"/> on success;
    /// HTTP 400 when the request is invalid;
    /// HTTP 500 on unexpected error.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the provided name is invalid; translated to HTTP 400.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ContactCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] ContactCategoryCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.Name, ct);
            return Created($"/api/contact-categories/{created.Id}", created);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create category failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets a single contact category by id for the current user.
    /// </summary>
    /// <param name="id">Identifier of the contact category to retrieve.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 200 with <see cref="ContactCategoryDto"/> when found; HTTP 404 when not found; HTTP 500 on unexpected error.</returns>
    /// <exception cref="Exception">Thrown when an unexpected server error occurs while retrieving the category.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get category failed {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates the name of a contact category owned by the current user.
    /// </summary>
    /// <param name="id">Identifier of the contact category to update.</param>
    /// <param name="req">Update payload containing the new name.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 on success; HTTP 404 when the category is not found; HTTP 400 when the request is invalid; HTTP 500 on unexpected error.</returns>
    /// <exception cref="ArgumentException">Thrown when the category is not found or input is invalid; translated to HTTP 404/400.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] ContactCategoryUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            await _svc.UpdateAsync(id, _current.UserId, req.Name, ct);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Update failed for contact category {CategoryId}", id);
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update failed for contact category {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a contact category owned by the current user.
    /// </summary>
    /// <param name="id">Identifier of the contact category to delete.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 on success; HTTP 404 when the category is not found; HTTP 500 on unexpected error.</returns>
    /// <exception cref="ArgumentException">Thrown when the category cannot be deleted; translated to HTTP 404.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete failed for contact category {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Assigns a symbol attachment to the specified contact category.
    /// </summary>
    /// <param name="id">Identifier of the contact category.</param>
    /// <param name="attachmentId">Identifier of the attachment to assign as symbol.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 on success; HTTP 404 when the category or attachment is not found; HTTP 500 on unexpected error.</returns>
    /// <exception cref="ArgumentException">Thrown when provided identifiers are invalid; translated to HTTP 404.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetSymbol failed for contact category {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Clears any symbol attachment from the specified contact category.
    /// </summary>
    /// <param name="id">Identifier of the contact category.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>HTTP 204 on success; HTTP 404 when the category is not found; HTTP 500 on unexpected error.</returns>
    /// <exception cref="ArgumentException">Thrown when the category id is invalid; translated to HTTP 404.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for contact category {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}