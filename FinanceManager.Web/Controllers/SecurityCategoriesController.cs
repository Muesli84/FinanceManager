using System.ComponentModel.DataAnnotations;
using FinanceManager.Application;
using FinanceManager.Application.Securities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/security-categories")]
[Authorize]
public sealed class SecurityCategoriesController : ControllerBase
{
    private readonly ISecurityCategoryService _service;
    private readonly ICurrentUserService _current;

    public SecurityCategoriesController(ISecurityCategoryService service, ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    public sealed class CategoryRequest
    {
        [Required, MinLength(2)]
        public string Name { get; set; } = string.Empty;
    }

    // WICHTIG: Route eindeutig benennen, damit CreatedAtRoute sie sicher findet.
    [HttpGet("{id:guid}", Name = "GetSecurityCategory")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
        => Ok(await _service.ListAsync(_current.UserId, ct));

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] CategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, ct);

        // Verwendet jetzt den eindeutig benannten Route-Namen statt CreatedAtAction (vermeidet „No route matches…“)
        return CreatedAtRoute("GetSecurityCategory", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] CategoryRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }
}