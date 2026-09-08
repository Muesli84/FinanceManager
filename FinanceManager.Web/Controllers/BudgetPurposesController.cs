using FinanceManager.Application;
using FinanceManager.Application.Budget;
using FinanceManager.Application.Common;
using FinanceManager.Application.Exceptions;
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
/// Manages budget purposes for the current user.
/// </summary>
[ApiController]
[Route("api/budget/purposes")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetPurposesController : ControllerBase
{
    private const string Origin = "API_BudgetPurpose";

    private readonly IBudgetPurposeService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<BudgetPurposesController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="svc">The svc.</param>
    /// <param name="current">The current.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">The localizer.</param>
    public BudgetPurposesController(
        IBudgetPurposeService svc,
        ICurrentUserService current,
        ILogger<BudgetPurposesController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists purposes for the current user.
    /// When <paramref name="from"/> and <paramref name="to"/> are provided, returns an overview including rule count and budget sum.
    /// </summary>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="sourceType">The source type.</param>
    /// <param name="q">The q.</param>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetPurposeOverviewDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 200,
        [FromQuery] BudgetSourceType? sourceType = null,
        [FromQuery] string? q = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken ct = default)
    {
        try
        {
            var list = await _svc.ListOverviewAsync(_current.UserId, skip, take, sourceType, q, from, to, budgetCategoryId: null, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List budget purposes failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets a budget purpose by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BudgetPurposeDto), StatusCodes.Status200OK)]
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
            _logger.LogError(ex, "Get budget purpose failed {PurposeId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates a budget purpose.
    /// </summary>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BudgetPurposeDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] BudgetPurposeCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var created = await _svc.CreateAsync(_current.UserId, req.Name, req.SourceType, req.SourceId, req.Description, req.BudgetCategoryId, ct, req.ValuationType);
            return Created($"/api/budget/purposes/{created.Id}", created);
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
        catch (DomainValidationException ex)
        {
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create budget purpose failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates a budget purpose.
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
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] BudgetPurposeUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _svc.UpdateAsync(id, _current.UserId, req.Name, req.SourceType, req.SourceId, req.Description, req.BudgetCategoryId, ct, req.ValuationType);
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
        catch (DomainValidationException ex)
        {
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update budget purpose failed {PurposeId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a budget purpose.
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
            _logger.LogError(ex, "Delete budget purpose failed {PurposeId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
