using FinanceManager.Application;
using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers.Contacts;

/// <summary>
/// Provides CRUD endpoints for contacts owned by the current user and related operations (aliases, merge, symbols).
/// The controller delegates business logic to <see cref="IContactService"/>.
/// </summary>
[ApiController]
[Route("api/contacts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactsController : ControllerBase
{
    private readonly IContactService _contacts;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ContactsController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="ContactsController"/>.
    /// </summary>
    /// <param name="contacts">Contact service.</param>
    /// <param name="current">Current user service.</param>
    /// <param name="logger">Logger.</param>
    public ContactsController(IContactService contacts, ICurrentUserService current, ILogger<ContactsController> logger)
    {
        _contacts = contacts;
        _current = current;
        _logger = logger;
    }

    /// <summary>
    /// Request payload to create a contact.
    /// </summary>
    public sealed record ContactCreateRequest([Required, MinLength(2)] string Name, ContactType Type, Guid? CategoryId, string? Description, bool? IsPaymentIntermediary);

    /// <summary>
    /// Request payload to update a contact.
    /// </summary>
    public sealed record ContactUpdateRequest([Required, MinLength(2)] string Name, ContactType Type, Guid? CategoryId, string? Description, bool? IsPaymentIntermediary);

    /// <summary>
    /// Request payload to add an alias to a contact.
    /// </summary>
    public sealed record AliasCreateRequest([Required, MinLength(1)] string Pattern);

    /// <summary>
    /// Request payload to merge contacts.
    /// </summary>
    public sealed record ContactMergeRequest([Required] Guid TargetContactId);

    /// <summary>
    /// Lists contacts for the current user with optional filtering and pagination.
    /// </summary>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take.</param>
    /// <param name="type">Optional contact type filter.</param>
    /// <param name="all">If true, ignore pagination and return all items.</param>
    /// <param name="nameFilter">Optional name filter (query param 'q').</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="ContactDto"/>.</returns>
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
        if (all)
        {
            skip = 0;
            take = hardMax;
        }
        take = Math.Clamp(take, 1, hardMax);

        try
        {
            var list = await _contacts.ListAsync(_current.UserId, skip, take, type, nameFilter, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List contacts failed (skip={Skip}, take={Take}, type={Type}, all={All}, q={Q})",
                skip, take, type, all, nameFilter);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Retrieves a single contact by id if it belongs to the current user.
    /// </summary>
    /// <param name="id">Contact identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Contact DTO or 404.</returns>
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
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Creates a new contact for the current user.
    /// </summary>
    /// <param name="req">Creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created contact or 400 on validation errors.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] ContactCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await _contacts.CreateAsync(_current.UserId, req.Name, req.Type, req.CategoryId, req.Description, req.IsPaymentIntermediary, ct);
            return CreatedAtRoute("GetContact", new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create contact failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Updates an existing contact owned by the current user.
    /// </summary>
    /// <param name="id">Contact identifier.</param>
    /// <param name="req">Update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated contact or 404 if not found.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] ContactUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var updated = await _contacts.UpdateAsync(id, _current.UserId, req.Name, req.Type, req.CategoryId, req.Description, req.IsPaymentIntermediary, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update contact {ContactId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a contact owned by the current user.
    /// </summary>
    /// <param name="id">Contact identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or NotFound when missing.</returns>
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
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns alias list for a contact.
    /// </summary>
    [HttpGet("{id:guid}/aliases")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetAliasAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var aliases = await _contacts.ListAliases(id, _current.UserId, ct);
            return Ok(aliases);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get contact {ContactId} aliases failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Adds an alias to a contact.
    /// </summary>
    [HttpPost("{id:guid}/aliases")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> AddAliasAsync(Guid id, [FromBody] AliasCreateRequest req, CancellationToken ct)
    {
        try
        {
            await _contacts.AddAliasAsync(id, _current.UserId, req.Pattern, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Add contact {ContactId} alias failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }   
    }

    /// <summary>
    /// Deletes an alias from a contact.
    /// </summary>
    [HttpDelete("{id:guid}/aliases/{aliasId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteAliasAsync(Guid id, Guid aliasId, CancellationToken ct)
    {
        try
        {
            await _contacts.DeleteAliasAsync(id, _current.UserId, aliasId, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete contact {ContactId} alias failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Merges a contact into another contact owned by the same user.
    /// </summary>
    [HttpPost("{id:guid}/merge")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MergeAsync(Guid id, [FromBody] ContactMergeRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        try
        {
            var dto = await _contacts.MergeAsync(_current.UserId, id, req.TargetContactId, ct);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge contacts failed (source={Source}, target={Target})", id, req.TargetContactId);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Returns count of contacts for the current user.
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> CountAsync(CancellationToken ct = default)
    {
        try
        {
            var count = await _contacts.CountAsync(_current.UserId, ct);
            return Ok(new { count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Count contacts failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Sets a symbol attachment for the contact.
    /// </summary>
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
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SetSymbol failed for contact {ContactId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetSymbol failed for contact {ContactId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Clears the symbol attachment for the contact.
    /// </summary>
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
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "ClearSymbol failed for contact {ContactId}", id);
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClearSymbol failed for contact {ContactId}", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

}

