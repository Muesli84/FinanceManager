using FinanceManager.Application;
using FinanceManager.Application.Securities;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecuritiesController : ControllerBase
{
    private readonly ISecurityService _service;
    private readonly ICurrentUserService _current;
    public SecuritiesController(ISecurityService service, ICurrentUserService current)
    {
        _service = service; _current = current;
    }

    public sealed class SecurityRequest
    {
        [Required, MinLength(2)] public string Name { get; set; } = string.Empty;
        [Required, MinLength(3)] public string Identifier { get; set; } = string.Empty;
        [Required, MinLength(3)] public string CurrencyCode { get; set; } = "EUR";
        public string? Description { get; set; }
        public string? AlphaVantageCode { get; set; }
        public Guid? CategoryId { get; set; }            // NEW
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await _service.ListAsync(_current.UserId, onlyActive, ct));

    [HttpGet("count")]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    [HttpGet("{id:guid}", Name = "GetSecurityAsync")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        // FIX: Use CreatedAtRoute because we referenced the named route (not the action method name) before.
        return CreatedAtRoute("GetSecurityAsync", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }
}
