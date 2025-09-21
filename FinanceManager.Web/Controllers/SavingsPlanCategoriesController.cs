using FinanceManager.Application;
using FinanceManager.Application.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/savings-plan-categories")]
public sealed class SavingsPlanCategoriesController : ControllerBase
{
    private readonly ISavingsPlanCategoryService _service;
    private readonly ICurrentUserService _current;

    public SavingsPlanCategoriesController(ISavingsPlanCategoryService service, ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    [HttpGet]
    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(CancellationToken ct)
        => await _service.ListAsync(_current.UserId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SavingsPlanCategoryDto>> GetAsync(Guid id, CancellationToken ct)
        => await _service.GetAsync(id, _current.UserId, ct) is { } dto ? dto : NotFound();

    [HttpPost]
    public async Task<ActionResult<SavingsPlanCategoryDto>> CreateAsync([FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.CreateAsync(_current.UserId, dto.Name, ct);

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavingsPlanCategoryDto>> UpdateAsync(Guid id, [FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.UpdateAsync(id, _current.UserId, dto.Name, ct) is { } updated ? updated : NotFound();

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, _current.UserId, ct) ? NoContent() : NotFound();
}