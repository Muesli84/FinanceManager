using FinanceManager.Application;
using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/contacts")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ContactsController : ControllerBase
{
    private readonly IContactService _contacts;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ContactsController> _logger;

    public ContactsController(IContactService contacts, ICurrentUserService current, ILogger<ContactsController> logger)
    {
        _contacts = contacts;
        _current = current;
        _logger = logger;
    }

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

    [HttpPost]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
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
            return BadRequest(new ApiErrorDto(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create contact failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

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
            return BadRequest(new ApiErrorDto(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update contact {ContactId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

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

    [HttpPost("{id:guid}/merge")]
    [ProducesResponseType(typeof(ContactDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
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
            return BadRequest(new ApiErrorDto(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Merge contacts failed (source={Source}, target={Target})", id, req.TargetContactId);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    public sealed record CountResponse(int count);

    [HttpGet("count")]
    public async Task<IActionResult> CountAsync(CancellationToken ct = default)
    {
        try
        {
            var count = await _contacts.CountAsync(_current.UserId, ct);
            return Ok(new CountResponse(count));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Count contacts failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

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
