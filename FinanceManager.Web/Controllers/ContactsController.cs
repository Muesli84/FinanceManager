using FinanceManager.Application;
using FinanceManager.Application.Common;
using FinanceManager.Application.Contacts;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Contacts;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages contacts for the current user: list, detail, create, update, delete, count, aliases,
/// merging and symbol assignment.
/// </summary>
[ApiController]
[Route("api/contacts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactsController : ControllerBase
{
    private const string Origin = "API_Contacts";
    private const string ParentAssignmentConflictCode = "Err_Conflict_ParentAssignment";

    private readonly IContactService _contacts;
    private readonly ICurrentUserService _current;
    private readonly IParentAssignmentService _parentAssign;
    private readonly ILogger<ContactsController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance of <see cref="ContactsController"/>.
    /// </summary>
    /// <param name="contacts">Contact service.</param>
    /// <param name="current">Current user context.</param>
    /// <param name="parentAssign">Parent assignment service.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="localizer">Shared controller string localizer.</param>
    public ContactsController(
        IContactService contacts,
        ICurrentUserService current,
        IParentAssignmentService parentAssign,
        ILogger<ContactsController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _contacts = contacts;
        _current = current;
        _parentAssign = parentAssign;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists contacts with optional paging, filtering by type and name, or returning all if <c>all=true</c>.
    /// </summary>
    /// <param name="skip">Items to skip.</param>
    /// <param name="take">Maximum items to return (ignored when all=true).</param>
    /// <param name="type">Optional contact type filter.</param>
    /// <param name="all">If true returns all contacts ignoring paging.</param>
    /// <param name="nameFilter">Optional name filter (substring).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a read-only list of <see cref="ContactDto"/> instances.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ContactDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] ContactType? type = null,
        [FromQuery] bool all = false,
        [FromQuery(Name = "q")] string? nameFilter = null,
        CancellationToken ct = default)
    {
        int hardMax = int.MaxValue;
        if (all) { skip = 0; take = hardMax; }
        take = Math.Clamp(take, 1, hardMax);
        try
        {
            var list = await _contacts.ListAsync(_current.UserId, skip, take, type, nameFilter, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List contacts failed (skip={Skip}, take={Take}, type={Type}, all={All}, q={Q})", skip, take, type, all, nameFilter);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets a single contact by id.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the <see cref="ContactDto"/> when found; 404 Not Found otherwise.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}", Name = "GetContact")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var contact = await _contacts.GetAsync(id, _current.UserId, ct);
            return contact is null ? NotFound() : Ok(contact);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get contact {ContactId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates a new contact.
    /// </summary>
    /// <param name="req">Creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 201 Created with the created <see cref="ContactDto"/> when successful;
    /// 400 Bad Request when input is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when input arguments are invalid (mapped to 400).</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] ContactCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var created = await _contacts.CreateAsync(_current.UserId, req.Name, req.Type, req.CategoryId, req.Description, req.IsPaymentIntermediary, ct);

            if (req.Parent != null)
            {
                var assignmentSucceeded = await _parentAssign.TryAssignAsync(
                    _current.UserId,
                    req.Parent,
                    createdKind: "contacts",
                    createdId: created.Id,
                    ct);

                _logger.LogInformation(
                    "Contact parent assignment result: OwnerUserId={OwnerUserId}, ParentKind={ParentKind}, ParentId={ParentId}, ParentField={ParentField}, ContactId={ContactId}, AssignmentResult={AssignmentResult}, TraceId={TraceId}",
                    _current.UserId,
                    req.Parent.ParentKind,
                    req.Parent.ParentId,
                    req.Parent.Field,
                    created.Id,
                    assignmentSucceeded ? "assigned" : "failed",
                    HttpContext.TraceIdentifier);

                if (!assignmentSucceeded)
                {
                    var rollbackSucceeded = await _contacts.DeleteAsync(created.Id, _current.UserId, ct);
                    _logger.LogWarning(
                        "Rolling back contact create after failed parent assignment: OwnerUserId={OwnerUserId}, ParentKind={ParentKind}, ParentId={ParentId}, ContactId={ContactId}, RollbackSucceeded={RollbackSucceeded}, TraceId={TraceId}",
                        _current.UserId,
                        req.Parent.ParentKind,
                        req.Parent.ParentId,
                        created.Id,
                        rollbackSucceeded,
                        HttpContext.TraceIdentifier);

                    var localized = _localizer[$"{Origin}_{ParentAssignmentConflictCode}"];
                    var message = localized.ResourceNotFound
                        ? "Contact creation could not be completed because assignment to the requested entry failed."
                        : localized.Value;

                    return Conflict(ApiErrorDto.Create(Origin, ParentAssignmentConflictCode, message));
                }
            }

            return CreatedAtRoute("GetContact", new { id = created.Id }, created);
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
            _logger.LogError(ex, "Create contact failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates an existing contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the updated <see cref="ContactDto"/> when found; 404 Not Found otherwise.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] ContactUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _contacts.UpdateAsync(id, _current.UserId, req.Name, req.Type, req.CategoryId, req.Description, req.IsPaymentIntermediary, ct);
            return updated is null ? NotFound() : Ok(updated);
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
            _logger.LogError(ex, "Update contact {ContactId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a contact owned by the current user.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when deletion succeeded; 404 Not Found when the contact does not exist.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _contacts.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete contact {ContactId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Lists all alias patterns for a contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the list of alias patterns.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("{id:guid}/aliases")]
    [ProducesResponseType(typeof(IReadOnlyList<AliasNameDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAliasAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var aliases = await _contacts.ListAliases(id, _current.UserId, ct);
            return Ok(aliases);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex) { _logger.LogError(ex, "Get contact {ContactId} aliases failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Adds a new alias pattern to the contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="req">Alias creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when the alias was added successfully.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpPost("{id:guid}/aliases")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddAliasAsync(Guid id, [FromBody] AliasCreateRequest req, CancellationToken ct)
    {
        try
        {
            await _contacts.AddAliasAsync(id, _current.UserId, req.Pattern, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex) { _logger.LogError(ex, "Add contact {ContactId} alias failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Deletes an alias from the contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="aliasId">Alias id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when the alias was deleted successfully.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpDelete("{id:guid}/aliases/{aliasId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAliasAsync(Guid id, Guid aliasId, CancellationToken ct)
    {
        try
        {
            await _contacts.DeleteAliasAsync(id, _current.UserId, aliasId, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex) { _logger.LogError(ex, "Delete contact {ContactId} alias failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Merges one contact into another target contact.
    /// </summary>
    /// <param name="id">Source contact id.</param>
    /// <param name="req">Merge request containing target id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the merged <see cref="ContactDto"/> when successful; 400 Bad Request when arguments are invalid.</returns>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid (mapped to 400).</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("{id:guid}/merge")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MergeAsync(Guid id, [FromBody] FinanceManager.Shared.Dtos.Contacts.ContactMergeRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var dto = await _contacts.MergeAsync(_current.UserId, id, req.TargetContactId, ct, req.Preference);
            return Ok(dto);
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
            _logger.LogError(ex, "Merge contacts failed (source={Source}, target={Target})", id, req.TargetContactId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Simple count endpoint returning number of contacts owned by user.
    /// </summary>
    /// <param name="count">Total number of contacts.</param>
    /// <returns>The result.</returns>
    public sealed record CountResponse(int count);

    /// <summary>
    /// Returns total number of contacts for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a <see cref="CountResponse"/> containing the total count.</returns>
    [HttpGet("count")]
    public async Task<IActionResult> CountAsync(CancellationToken ct = default)
    {
        try { var count = await _contacts.CountAsync(_current.UserId, ct); return Ok(new CountResponse(count)); }
        catch (Exception ex) { _logger.LogError(ex, "Count contacts failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Assigns a symbol attachment to the contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when the assignment succeeded; 404 Not Found when the contact was not found.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _contacts.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SetSymbol failed for contact {ContactId}", id);
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetSymbol failed for contact {ContactId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Clears any symbol attachment from the contact.
    /// </summary>
    /// <param name="id">Contact id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when the clear succeeded; 404 Not Found when the contact was not found.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _contacts.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "ClearSymbol failed for contact {ContactId}", id);
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for contact {ContactId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
