using FinanceManager.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/savings-plan-categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
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
    [ProducesResponseType(typeof(IReadOnlyList<SavingsPlanCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(CancellationToken ct)
        => await _service.ListAsync(_current.UserId, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> GetAsync(Guid id, CancellationToken ct)
        => await _service.GetAsync(id, _current.UserId, ct) is { } dto ? dto : NotFound();

    [HttpPost]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> CreateAsync([FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.CreateAsync(_current.UserId, dto.Name, ct);

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> UpdateAsync(Guid id, [FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.UpdateAsync(id, _current.UserId, dto.Name, ct) is { } updated ? updated : NotFound();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, _current.UserId, ct) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }
}