using FinanceManager.Application;
using FinanceManager.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/admin/ip-blocks")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AdminIpBlocksController : ControllerBase
{
    private readonly IIpBlockService _service;
    private readonly ICurrentUserService _current;
    private readonly ILogger<AdminIpBlocksController> _logger;

    public AdminIpBlocksController(IIpBlockService service, ICurrentUserService current, ILogger<AdminIpBlocksController> logger)
    {
        _service = service; _current = current; _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<IpBlockDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool? onlyBlocked, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var list = await _service.ListAsync(onlyBlocked, ct);
        return Ok(list);
    }

    [HttpPost]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] IpBlockCreateRequest req, CancellationToken ct)
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

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(IpBlockDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] IpBlockUpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.UpdateAsync(id, req.Reason, req.IsBlocked, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost("{id:guid}/block")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BlockAsync(Guid id, [FromBody] IpBlockUpdateRequest req, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.BlockAsync(id, req.Reason, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/unblock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnblockAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.UnblockAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/reset-counters")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        if (!_current.IsAdmin) return Forbid();
        var ok = await _service.ResetCountersAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

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
