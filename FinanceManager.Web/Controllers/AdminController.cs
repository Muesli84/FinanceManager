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

    public AdminController(IUserAdminService userSvc, IIpBlockService ipSvc, ICurrentUserService current, ILogger<AdminController> logger)
    { _userSvc = userSvc; _ipSvc = ipSvc; _current = current; _logger = logger; }

    /// <summary>
    /// Returns all users (no paging by design for admin view).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of users.</returns>
    [HttpGet("users")]
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
    /// <returns>User details or NotFound.</returns>
    [HttpGet("users/{id:guid}", Name = "GetAdminUser")]
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
    /// <returns>Created user details.</returns>
    [HttpPost("users")]
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
    /// <returns>Updated user details or NotFound.</returns>
    [HttpPut("users/{id:guid}")]
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
    /// <returns>NoContent or NotFound.</returns>
    [HttpPost("users/{id:guid}/reset-password")]
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
    /// <returns>NoContent or NotFound.</returns>
    [HttpPost("users/{id:guid}/unlock")]
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
    /// <returns>NoContent or NotFound.</returns>
    [HttpDelete("users/{id:guid}")]
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
    /// <returns>List of IP blocks.</returns>
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
    /// <returns>Created IP block details.</returns>
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
    /// <returns>IP block details or NotFound.</returns>
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
    /// <returns>Updated IP block details or NotFound.</returns>
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
    /// <returns>NoContent or NotFound.</returns>
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
    /// <returns>NoContent or NotFound.</returns>
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
    /// <returns>NoContent or NotFound.</returns>
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
    /// <returns>NoContent or NotFound.</returns>
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
