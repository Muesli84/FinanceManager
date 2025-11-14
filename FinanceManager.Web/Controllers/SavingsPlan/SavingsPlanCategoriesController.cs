using FinanceManager.Application;
using FinanceManager.Application.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.SavingsPlan; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Manages savings plan categories for the current user (CRUD and symbol attachments).
/// </summary>
[ApiController]
[Route("api/savings-plan-categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlanCategoriesController : ControllerBase
{
    private readonly ISavingsPlanCategoryService _service;
    private readonly ICurrentUserService _current;

    /// <summary>
    /// Creates a new instance of <see cref="SavingsPlanCategoriesController"/>.
    /// </summary>
    public SavingsPlanCategoriesController(ISavingsPlanCategoryService service, ICurrentUserService current)
    {
        _service = service;
        _current = current;
    }

    /// <summary>
    /// Lists savings plan categories for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(CancellationToken ct)
        => await _service.ListAsync(_current.UserId, ct);

    /// <summary>
    /// Gets a single savings plan category by id for the current user.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SavingsPlanCategoryDto>> GetAsync(Guid id, CancellationToken ct)
        => await _service.GetAsync(id, _current.UserId, ct) is { } dto ? dto : NotFound();

    /// <summary>
    /// Creates a new savings plan category.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SavingsPlanCategoryDto>> CreateAsync([FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.CreateAsync(_current.UserId, dto.Name, ct);

    /// <summary>
    /// Updates an existing savings plan category.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavingsPlanCategoryDto>> UpdateAsync(Guid id, [FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.UpdateAsync(id, _current.UserId, dto.Name, ct) is { } updated ? updated : NotFound();

    /// <summary>
    /// Deletes a savings plan category.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, _current.UserId, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Sets the symbol attachment for the category.
    /// </summary>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
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

    /// <summary>
    /// Clears the symbol attachment for the category.
    /// </summary>
    [HttpDelete("{id:guid}/symbol")]
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
