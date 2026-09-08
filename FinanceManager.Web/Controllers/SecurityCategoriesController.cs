using FinanceManager.Application;
using FinanceManager.Application.Securities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages security categories (CRUD and symbol attachment) for the current user.
/// </summary>
[ApiController]
[Route("api/security-categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityCategoriesController : ControllerBase
{
    private readonly ISecurityCategoryService _service;
    private readonly ICurrentUserService _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityCategoriesController"/> class.
    /// </summary>
    /// <param name="service">The security category service used to perform CRUD operations.</param>
    /// <param name="current">Service that provides information about the currently authenticated user.</param>
    public SecurityCategoriesController(ISecurityCategoryService service, ICurrentUserService current)
    { _service = service; _current = current; }

    /// <summary>
    /// Gets a single security category by id.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains a <see cref="SecurityCategoryDto"/> and a 200 OK status when found,
    /// or a 404 Not Found when the category does not exist or does not belong to the current user.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}", Name = "GetSecurityCategory")]
    [ProducesResponseType(typeof(SecurityCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Lists all security categories for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing a 200 OK response with a list of <see cref="SecurityCategoryDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _service.ListAsync(_current.UserId, ct));

    /// <summary>
    /// Creates a new security category.
    /// </summary>
    /// <param name="req">Category creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains the created <see cref="SecurityCategoryDto"/> and a 201 Created status.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="req"/> is null.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SecurityCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, ct);
        return CreatedAtRoute("GetSecurityCategory", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing security category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="req">Update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains the updated <see cref="SecurityCategoryDto"/> and a 200 OK status when the update succeeds,
    /// or a 404 Not Found when the category does not exist or does not belong to the current user.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="req"/> is null.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SecurityCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SecurityCategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Deletes a security category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> with 204 No Content when deletion succeeds or 404 Not Found when not found.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Assigns a symbol attachment to the security category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> with 204 No Content when the symbol was set successfully.
    /// Returns 404 Not Found when the category or attachment cannot be found or is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown by the underlying service when arguments are invalid (mapped to 404).</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }

    /// <summary>
    /// Clears any symbol attachment from the security category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> with 204 No Content when the symbol was cleared successfully.
    /// Returns 404 Not Found when the category cannot be found or the operation is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown by the underlying service when arguments are invalid (mapped to 404).</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }
}