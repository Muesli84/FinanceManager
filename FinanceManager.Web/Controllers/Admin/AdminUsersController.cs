using System.ComponentModel.DataAnnotations;
using System.Net.Mime;
using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Admin;

/// <summary>
/// Administrative endpoints to manage users (create, update, reset password, unlock, delete).
/// Intended for internal administration UI and requires appropriate authorization at the API gateway.
/// </summary>
[ApiController]
[Route("api/admin/users")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IUserAdminService _svc;
    private readonly ILogger<AdminUsersController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="AdminUsersController"/>.
    /// </summary>
    /// <param name="svc">Service to perform user administration operations.</param>
    /// <param name="logger">Logger.</param>
    public AdminUsersController(IUserAdminService svc, ILogger<AdminUsersController> logger)
    { _svc = svc; _logger = logger; }

    /// <summary>
    /// Returns the list of users in the system.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="UserAdminDto"/> or 500 on unexpected error.</returns>
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

    /// <summary>
    /// Retrieves a single user by id.
    /// </summary>
    /// <param name="id">User identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="UserAdminDto"/> or 404 if not found.</returns>
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

    /// <summary>
    /// Request payload to create a new user.
    /// </summary>
    public sealed record CreateUserRequest([
        Required, MinLength(3)] string Username,
        [Required, MinLength(6)] string Password,
        bool IsAdmin);

    /// <summary>
    /// Request payload to update an existing user.
    /// </summary>
    public sealed record UpdateUserRequest(
        [MinLength(3)] string? Username,
        bool? IsAdmin,
        bool? Active,
        string? PreferredLanguage);

    /// <summary>
    /// Request payload to reset a user's password.
    /// </summary>
    public sealed record ResetPasswordRequest([Required, MinLength(6)] string NewPassword);

    /// <summary>
    /// Creates a new user account.
    /// </summary>
    /// <param name="req">Creation request containing username, password and admin flag.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created <see cref="UserAdminDto"/>, 400/409 on validation or business errors.</returns>
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

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    /// <param name="id">User identifier to update.</param>
    /// <param name="req">Update request with optional fields.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="UserAdminDto"/>, 404 if not found, 400/409 on validation or business errors.</returns>
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

    /// <summary>
    /// Resets the password for the specified user.
    /// </summary>
    /// <param name="id">User identifier.</param>
    /// <param name="req">New password payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success, 404 if user not found, 400 for invalid input.</returns>
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

    /// <summary>
    /// Unlocks a locked user account.
    /// </summary>
    /// <param name="id">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success, 404 if user not found.</returns>
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

    /// <summary>
    /// Deletes a user from the system.
    /// </summary>
    /// <param name="id">User identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success, 404 if user not found.</returns>
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

