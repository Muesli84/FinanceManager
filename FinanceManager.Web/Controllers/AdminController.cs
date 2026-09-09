using FinanceManager.Application;
using FinanceManager.Application.Security;
using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Administrative endpoints for managing users and IP blocks.
/// Provides CRUD for users (including password reset, unlock) and operations on IP block entries.
/// </summary>
[ApiController]
[Route("api/admin")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminController : ControllerBase
{
    private readonly IUserAdminService _userSvc;
    private readonly IIpBlockService _ipSvc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AdminController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="AdminController"/>.
    /// </summary>
    /// <param name="userSvc">Service for user administration operations.</param>
    /// <param name="ipSvc">Service for IP block management.</param>
    /// <param name="current">Service providing information about the current user/context.</param>
    /// <param name="logger">Logger instance for the controller.</param>
    public AdminController(IUserAdminService userSvc, IIpBlockService ipSvc, ICurrentUserService current, ILogger<AdminController> logger)
    { _userSvc = userSvc; _ipSvc = ipSvc; _current = current; _logger = logger; }

    /// <summary>
    /// Returns all users (no paging by design for admin view).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with a list of <see cref="UserAdminDto"/> on success; HTTP 500 on unexpected error.</returns>
    /// <exception cref="Exception">Thrown when an unexpected error occurs while listing users.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("users")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(typeof(IReadOnlyList<UserAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsersAsync(CancellationToken ct)
    {
        try { var list = await _userSvc.ListAsync(ct); return Ok(list); }
        catch (Exception ex) { _logger.LogError(ex, "List users failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Gets a single user by id.
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with <see cref="UserAdminDto"/> when found; HTTP 404 when not found; HTTP 500 on unexpected error.</returns>
    /// <exception cref="Exception">Thrown when an unexpected error occurs while retrieving the user.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("users/{id:guid}", Name = "GetAdminUser")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAsync(Guid id, CancellationToken ct)
    {
        try { var user = await _userSvc.GetAsync(id, ct); return user is null ? NotFound() : Ok(user); }
        catch (Exception ex) { _logger.LogError(ex, "Get user {UserId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Creates a new user account.
    /// </summary>
    /// <param name="req">Creation payload (username, password, admin flag).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 201 with created <see cref="UserAdminDto"/> on success; HTTP 400 for validation errors; HTTP 409 when user already exists.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a user with the same username already exists.</exception>
    /// <exception cref="ArgumentException">Thrown when creation parameters are invalid.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPost("users")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUserAsync([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var created = await _userSvc.CreateAsync(req.Username, req.Password, req.IsAdmin, ct); return CreatedAtRoute("GetAdminUser", new { id = created.Id }, created); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { _logger.LogError(ex, "Create user failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Updates basic user data (username, admin flag, active, preferred language).
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with updated <see cref="UserAdminDto"/> when successful; HTTP 404 when user not found; HTTP 409 on conflict; HTTP 400 on invalid args.</returns>
    /// <exception cref="InvalidOperationException">Thrown when update causes a conflict (e.g. username already in use).</exception>
    /// <exception cref="ArgumentException">Thrown when provided arguments are invalid.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPut("users/{id:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUserAsync(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var updated = await _userSvc.UpdateAsync(id, req.Username, req.IsAdmin, req.Active, req.PreferredLanguage, ct); return updated is null ? NotFound() : Ok(updated); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { _logger.LogError(ex, "Update user {UserId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Resets the password of a user.
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="req">New password payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when password reset succeeded; HTTP 404 when user not found; HTTP 400 when request invalid.</returns>
    /// <exception cref="ArgumentException">Thrown when provided arguments are invalid.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("users/{id:guid}/reset-password")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var ok = await _userSvc.ResetPasswordAsync(id, req.NewPassword, ct); return ok ? NoContent() : NotFound(); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) { _logger.LogError(ex, "Reset password {UserId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Unlocks a previously locked user account (e.g. after failed login attempts).
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when unlock succeeded; HTTP 404 when user not found.</returns>
    /// <exception cref="Exception">Thrown when an unexpected error occurs while unlocking the user.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("users/{id:guid}/unlock")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUserAsync(Guid id, CancellationToken ct)
    {
        try { var ok = await _userSvc.UnlockAsync(id, ct); return ok ? NoContent() : NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "Unlock user {UserId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Deletes a user account permanently.
    /// </summary>
    /// <param name="id">User id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when deletion succeeded; HTTP 404 when user not found.</returns>
    /// <exception cref="Exception">Thrown when an unexpected error occurs while deleting the user.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("users/{id:guid}")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUserAsync(Guid id, CancellationToken ct)
    {
        try { var ok = await _userSvc.DeleteAsync(id, ct); return ok ? NoContent() : NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "Delete user {UserId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Lists IP block entries (optionally only currently blocked ones).
    /// </summary>
    /// <param name="onlyBlocked">True to return only blocked entries.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with a list of <see cref="IpBlockDto"/> when authorized; HTTP 403 when current user is not admin.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("ip-blocks")]
    [ProducesResponseType(typeof(IReadOnlyList<IpBlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIpBlocksAsync([FromQuery] bool? onlyBlocked, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _ipSvc.ListAsync(onlyBlocked, ct);
        return Ok(list);
    }

    /// <summary>
    /// Creates a new IP block entry.
    /// </summary>
    /// <param name="req">Creation payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 201 with created <see cref="IpBlockDto"/> on success; HTTP 409 on conflict; HTTP 400 on invalid request.</returns>
    /// <exception cref="InvalidOperationException">Thrown when an equivalent block entry already exists.</exception>
    /// <exception cref="ArgumentException">Thrown when provided arguments are invalid.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPost("ip-blocks")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateIpBlockAsync([FromBody] IpBlockCreateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try { var dto = await _ipSvc.CreateAsync(req.IpAddress.Trim(), req.Reason, req.IsBlocked, ct); return CreatedAtRoute("GetIpBlock", new { id = dto.Id }, dto); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    /// <summary>
    /// Gets a single IP block entry by id.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with <see cref="IpBlockDto"/> when found; HTTP 404 when not found; HTTP 403 when not authorized.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("ip-blocks/{id:guid}", Name = "GetIpBlock")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIpBlockAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _ipSvc.ListAsync(null, ct);
        var dto = list.FirstOrDefault(x => x.Id == id);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Updates core properties of an IP block entry.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="req">Update payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 200 with updated <see cref="IpBlockDto"/> when successful; HTTP 404 when not found; HTTP 403 when not authorized.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("ip-blocks/{id:guid}")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateIpBlockAsync(Guid id, [FromBody] IpBlockUpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _ipSvc.UpdateAsync(id, req.Reason, req.IsBlocked, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Blocks an IP immediately.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="req">Reason payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when blocked; HTTP 404 when entry not found; HTTP 403 when not authorized.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("ip-blocks/{id:guid}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockIpAsync(Guid id, [FromBody] IpBlockUpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.BlockAsync(id, req.Reason, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Removes the block from an IP.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when unblocked; HTTP 404 when not found; HTTP 403 when not authorized.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("ip-blocks/{id:guid}/unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockIpAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.UnblockAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Resets counters (e.g. failed attempts) of an IP block entry.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when counters were reset; HTTP 404 when entry not found; HTTP 403 when not authorized.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("ip-blocks/{id:guid}/reset-counters")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.ResetCountersAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes an IP block entry.
    /// </summary>
    /// <param name="id">Entry id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>HTTP 204 when deleted; HTTP 404 when not found; HTTP 403 when not authorized.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("ip-blocks/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteIpBlockAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
