using System.ComponentModel.DataAnnotations;
using FinanceManager.Application.Savings;
using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/savings-plans")]
[Authorize]
public sealed class SavingsPlansController : ControllerBase
{
    private readonly ISavingsPlanService _service;
    private readonly FinanceManager.Application.ICurrentUserService _current;

    public SavingsPlansController(ISavingsPlanService service, FinanceManager.Application.ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    public sealed record SavingsPlanCreateRequest(
        [Required, MinLength(2)] string Name,
        SavingsPlanType Type,
        decimal? TargetAmount,
        DateTime? TargetDate,
        SavingsPlanInterval? Interval,
        Guid? CategoryId,
        string? ContractNumber
    );

    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        var list = await _service.ListAsync(_current.UserId, onlyActive, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}", Name = "GetSavingsPlans")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpGet("{id:guid}/analysis")]
    public async Task<IActionResult> AnalyzeAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.AnalyzeAsync(id, _current.UserId, ct);
        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);
        return CreatedAtRoute("GetSavingsPlans", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);
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