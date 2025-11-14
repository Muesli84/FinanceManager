using FinanceManager.Application;
using FinanceManager.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers.Admin;

/// <summary>
/// Administrative endpoints to manage IP blocks used for security (blocking abusive clients).
/// Requires an admin user.
/// </summary>
[ApiController]
[Route("api/admin/ip-blocks")] 
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminIpBlocksController : ControllerBase
{
    private readonly IIpBlockService _service;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AdminIpBlocksController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="AdminIpBlocksController"/>.
    /// </summary>
    /// <param name="service">IP block service.</param>
    /// <param name="current">Current user context service.</param>
    /// <param name="logger">Logger instance.</param>
    public AdminIpBlocksController(IIpBlockService service, ICurrentUserService current, ILogger<AdminIpBlocksController> logger)
    {
        _service = service; _current = current; _logger = logger;
    }

    /// <summary>
    /// Lists IP blocks. Admin only.
    /// </summary>
    /// <param name="onlyBlocked">Optional filter to return only currently blocked entries.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of <see cref="IpBlockDto"/> entries.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IpBlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool? onlyBlocked, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _service.ListAsync(onlyBlocked, ct);
        return Ok(list);
    }

    /// <summary>
    /// Request payload to create a new IP block entry.
    /// </summary>
    public sealed class CreateRequest
    {
        [Required, MaxLength(64)] public string IpAddress { get; set; } = string.Empty;
        [MaxLength(200)] public string? Reason { get; set; }
        public bool IsBlocked { get; set; } = true;
    }

    /// <summary>
    /// Creates a new IP block entry. Admin only.
    /// </summary>
    /// <param name="req">Create request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created <see cref="IpBlockDto"/> with 201 status or error status codes.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var dto = await _service.CreateAsync(req.IpAddress.Trim(), req.Reason, req.IsBlocked, ct);
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

    /// <summary>
    /// Retrieves a single IP block entry by id. Admin only.
    /// </summary>
    /// <param name="id">Identifier of the IP block entry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>IP block DTO or 404 if not found.</returns>
    [HttpGet("{id:guid}", Name = "GetIpBlock")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _service.ListAsync(null, ct);
        var dto = list.FirstOrDefault(x => x.Id == id);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Request payload to update an existing IP block entry.
    /// </summary>
    public sealed class UpdateRequest
    {
        [MaxLength(200)] public string? Reason { get; set; }
        public bool? IsBlocked { get; set; }
    }

    /// <summary>
    /// Updates an existing IP block entry. Admin only.
    /// </summary>
    /// <param name="id">IP block id.</param>
    /// <param name="req">Update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated DTO or 404 if not found.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.UpdateAsync(id, req.Reason, req.IsBlocked, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Blocks an IP entry (sets it to blocked). Admin only.
    /// </summary>
    /// <param name="id">IP block id.</param>
    /// <param name="req">Optional update request with reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or 404 when not found.</returns>
    [HttpPost("{id:guid}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockAsync(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.BlockAsync(id, req.Reason, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Unblocks an IP entry. Admin only.
    /// </summary>
    /// <param name="id">IP block id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or 404 when not found.</returns>
    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.UnblockAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Resets counters for an IP entry (e.g. failed attempts). Admin only.
    /// </summary>
    /// <param name="id">IP block id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or 404 when not found.</returns>
    [HttpPost("{id:guid}/reset-counters")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.ResetCountersAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes an IP block entry. Admin only.
    /// </summary>
    /// <param name="id">IP block id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or 404 when not found.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}

