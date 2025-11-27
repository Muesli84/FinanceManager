using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/admin/users")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _svc;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(IUserAdminService svc, ILogger<AdminUsersController> logger)
    { _svc = svc; _logger = logger; }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListAsync(ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List users failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpGet("{id:guid}", Name = "GetAdminUser")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var user = await _svc.GetAsync(id, ct);
            return user is null ? NotFound() : Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPost]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await _svc.CreateAsync(req.Username, req.Password, req.IsAdmin, ct);
            return CreatedAtRoute("GetAdminUser", new { id = created.Id }, created);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create user failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var updated = await _svc.UpdateAsync(id, req.Username, req.IsAdmin, req.Active, req.PreferredLanguage, ct);
            if (updated is null) return NotFound();
            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var ok = await _svc.ResetPasswordAsync(id, req.NewPassword, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset password {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _svc.UnlockAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlock user {UserId} failed", id);
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
            var ok = await _svc.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
