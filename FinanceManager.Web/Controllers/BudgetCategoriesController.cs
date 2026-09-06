using FinanceManager.Application;
using FinanceManager.Application.Budget;
using FinanceManager.Application.Common;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages budget categories for the current user.
/// </summary>
[ApiController]
[Route("api/budget/categories")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetCategoriesController : ControllerBase
{
    private const string Origin = "API_BudgetCategory";

    private readonly IBudgetCategoryService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<BudgetCategoriesController> _logger;
    private readonly IParentAssignmentService _parentAssign;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="svc">The svc.</param>
    /// <param name="current">The current.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="parentAssign">The parent assign.</param>
    /// <param name="localizer">The localizer.</param>
    public BudgetCategoriesController(
        IBudgetCategoryService svc,
        ICurrentUserService current,
        ILogger<BudgetCategoriesController> logger,
        IParentAssignmentService parentAssign,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
        _parentAssign = parentAssign;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists categories for the current user.
    /// </summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetCategoryOverviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, CancellationToken ct = default)
    {
        try
        {
            var list = await _svc.ListOverviewAsync(_current.UserId, from, to, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List budget categories failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets a category by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BudgetCategoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var dto = await _svc.GetAsync(id, _current.UserId, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get budget category failed {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates a category.
    /// </summary>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BudgetCategoryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] BudgetCategoryCreateRequest req, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.Name, ct);

            // Optional server-side parent assignment (create-and-assign)
            await _parentAssign.TryAssignAsync(
                _current.UserId,
                req.Parent,
                createdKind: "budget/categories",
                createdId: created.Id,
                ct);

            return Created($"/api/budget/categories/{created.Id}", created);
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.InnerExceptions)
            {
                ModelState.AddModelError(string.Empty, inner.Message);
            }

            return ValidationProblem(ModelState);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create budget category failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates a category.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] BudgetCategoryUpdateRequest req, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _svc.UpdateAsync(id, _current.UserId, req.Name, ct);
            return updated == null ? NotFound() : NoContent();
        }
        catch (AggregateException ex)
        {
            foreach (var inner in ex.InnerExceptions)
            {
                ModelState.AddModelError(string.Empty, inner.Message);
            }

            return ValidationProblem(ModelState);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update budget category failed {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a category.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var ok = await _svc.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete budget category failed {CategoryId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
