using FinanceManager.Application;
using FinanceManager.Application.Security;
using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

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

    // ---------------------------- USERS ----------------------------------
    // GET api/admin/users
    [HttpGet("users")]
    [ProducesResponseType(typeof(IReadOnlyList<UserAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsersAsync(CancellationToken ct)
    {
        try
        {
            var list = await _userSvc.ListAsync(ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List users failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // GET api/admin/users/{id}
    [HttpGet("users/{id:guid}", Name = "GetAdminUser")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var user = await _userSvc.GetAsync(id, ct);
            return user is null ? NotFound() : Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // POST api/admin/users
    [HttpPost("users")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUserAsync([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var created = await _userSvc.CreateAsync(req.Username, req.Password, req.IsAdmin, ct);
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

    // PUT api/admin/users/{id}
    [HttpPut("users/{id:guid}")]
    [ProducesResponseType(typeof(UserAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUserAsync(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var updated = await _userSvc.UpdateAsync(id, req.Username, req.IsAdmin, req.Active, req.PreferredLanguage, ct);
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

    // POST api/admin/users/{id}/reset-password
    [HttpPost("users/{id:guid}/reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPasswordAsync(Guid id, [FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var ok = await _userSvc.ResetPasswordAsync(id, req.NewPassword, ct);
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

    // POST api/admin/users/{id}/unlock
    [HttpPost("users/{id:guid}/unlock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnlockUserAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _userSvc.UnlockAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unlock user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // DELETE api/admin/users/{id}
    [HttpDelete("users/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUserAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _userSvc.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete user {UserId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // ---------------------------- IP BLOCKS -------------------------------
    // GET api/admin/ip-blocks
    [HttpGet("ip-blocks")]
    [ProducesResponseType(typeof(IReadOnlyList<IpBlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListIpBlocksAsync([FromQuery] bool? onlyBlocked, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _ipSvc.ListAsync(onlyBlocked, ct);
        return Ok(list);
    }

    // POST api/admin/ip-blocks
    [HttpPost("ip-blocks")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateIpBlockAsync([FromBody] IpBlockCreateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _ipSvc.CreateAsync(req.IpAddress.Trim(), req.Reason, req.IsBlocked, ct);
            return CreatedAtRoute("GetIpBlock", new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // GET api/admin/ip-blocks/{id}
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

    // PUT api/admin/ip-blocks/{id}
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

    // POST api/admin/ip-blocks/{id}/block
    [HttpPost("ip-blocks/{id:guid}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockIpAsync(Guid id, [FromBody] IpBlockUpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.BlockAsync(id, req.Reason, ct);
        return ok ? NoContent() : NotFound();
    }

    // POST api/admin/ip-blocks/{id}/unblock
    [HttpPost("ip-blocks/{id:guid}/unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockIpAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.UnblockAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    // POST api/admin/ip-blocks/{id}/reset-counters
    [HttpPost("ip-blocks/{id:guid}/reset-counters")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _ipSvc.ResetCountersAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    // DELETE api/admin/ip-blocks/{id}
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
