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
/// Manages budget overrides for the current user.
/// </summary>
[ApiController]
[Route("api/budget/overrides")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetOverridesController : ControllerBase
{
    private const string Origin = "API_BudgetOverride";

    private readonly IBudgetOverrideService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<BudgetOverridesController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="svc">The svc.</param>
    /// <param name="current">The current.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">The localizer.</param>
    public BudgetOverridesController(
        IBudgetOverrideService svc,
        ICurrentUserService current,
        ILogger<BudgetOverridesController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists overrides for a specific purpose.
    /// </summary>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("by-purpose/{budgetPurposeId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetOverrideDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByPurposeAsync(Guid budgetPurposeId, CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListByPurposeAsync(_current.UserId, budgetPurposeId, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List budget overrides failed {BudgetPurposeId}", budgetPurposeId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets an override by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BudgetOverrideDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var dto = await _svc.GetAsync(id, _current.UserId, ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get budget override failed {OverrideId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates an override.
    /// </summary>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BudgetOverrideDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] BudgetOverrideCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.BudgetPurposeId, req.Period, req.Amount, ct);
            return Created($"/api/budget/overrides/{created.Id}", created);
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
            _logger.LogError(ex, "Create budget override failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates an existing override.
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
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] BudgetOverrideUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _svc.UpdateAsync(id, _current.UserId, req.Period, req.Amount, ct);
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
            _logger.LogError(ex, "Update budget override failed {OverrideId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes an override.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _svc.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete budget override failed {OverrideId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
