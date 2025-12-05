using FinanceManager.Application;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Manages savings plan categories for the current user (CRUD and symbol attachment operations).
/// </summary>
[ApiController]
[Route("api/savings-plan-categories")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlanCategoriesController : ControllerBase
{
    private readonly ISavingsPlanCategoryService _service;
    private readonly ICurrentUserService _current;

    public SavingsPlanCategoriesController(ISavingsPlanCategoryService service, ICurrentUserService current)
    { _service = service; _current = current; }

    /// <summary>
    /// Lists all savings plan categories for the user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SavingsPlanCategoryDto>), StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(CancellationToken ct)
        => await _service.ListAsync(_current.UserId, ct);

    /// <summary>
    /// Gets a single category by id.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> GetAsync(Guid id, CancellationToken ct)
        => await _service.GetAsync(id, _current.UserId, ct) is { } dto ? dto : NotFound();

    /// <summary>
    /// Creates a new savings plan category.
    /// </summary>
    /// <param name="dto">Category data (only name is used).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> CreateAsync([FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.CreateAsync(_current.UserId, dto.Name, ct);

    /// <summary>
    /// Updates an existing savings plan category's name.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="dto">Category data (new name).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SavingsPlanCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SavingsPlanCategoryDto>> UpdateAsync(Guid id, [FromBody] SavingsPlanCategoryDto dto, CancellationToken ct)
        => await _service.UpdateAsync(id, _current.UserId, dto.Name, ct) is { } updated ? updated : NotFound();

    /// <summary>
    /// Deletes a savings plan category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
        => await _service.DeleteAsync(id, _current.UserId, ct) ? NoContent() : NotFound();

    /// <summary>
    /// Assigns a symbol attachment to the category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }

    /// <summary>
    /// Clears any symbol attachment from the category.
    /// </summary>
    /// <param name="id">Category id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }
}