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
/// Manages budget rules for the current user.
/// </summary>
[ApiController]
[Route("api/budget/rules")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetRulesController : ControllerBase
{
    private const string Origin = "API_BudgetRule";

    private readonly IBudgetRuleService _svc;
    private readonly ICurrentUserService _current;
    private readonly ILogger<BudgetRulesController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="svc">The svc.</param>
    /// <param name="current">The current.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">The localizer.</param>
    public BudgetRulesController(
        IBudgetRuleService svc,
        ICurrentUserService current,
        ILogger<BudgetRulesController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _svc = svc;
        _current = current;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists rules for a specific purpose.
    /// </summary>
    /// <param name="budgetPurposeId">The budget purpose id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("by-purpose/{budgetPurposeId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByPurposeAsync(Guid budgetPurposeId, CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListByPurposeAsync(_current.UserId, budgetPurposeId, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List budget rules failed {BudgetPurposeId}", budgetPurposeId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Gets a rule by id.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BudgetRuleDto), StatusCodes.Status200OK)]
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
            _logger.LogError(ex, "Get budget rule failed {RuleId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Creates a budget rule.
    /// </summary>
    /// <param name="req">Request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BudgetRuleDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] BudgetRuleCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            BudgetRuleDto created;

            var hasPurpose = req.BudgetPurposeId.HasValue && req.BudgetPurposeId.Value != Guid.Empty;
            var hasCategory = req.BudgetCategoryId.HasValue && req.BudgetCategoryId.Value != Guid.Empty;

            if (hasPurpose == hasCategory)
            {
                var ex = new ArgumentException("Exactly one of BudgetPurposeId or BudgetCategoryId must be provided", nameof(BudgetRuleCreateRequest.BudgetPurposeId));
                return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
            }

            if (hasPurpose)
            {
                created = await _svc.CreateAsync(_current.UserId, req.BudgetPurposeId!.Value, req.Amount, req.Interval, req.CustomIntervalMonths, req.StartDate, req.EndDate, req.PurposePattern, req.UseRegex, ct);
            }
            else
            {
                created = await _svc.CreateForCategoryAsync(_current.UserId, req.BudgetCategoryId!.Value, req.Amount, req.Interval, req.CustomIntervalMonths, req.StartDate, req.EndDate, req.PurposePattern, req.UseRegex, ct);
            }

            return Created($"/api/budget/rules/{created.Id}", created);
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
            // If the exception originates from PurposePattern regex validation, return a ModelState validation error
            if (string.Equals(ex.ParamName, "pattern", StringComparison.OrdinalIgnoreCase) || string.Equals(ex.ParamName, "PurposePattern", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(
                    nameof(BudgetRuleCreateRequest.PurposePattern),
                    _localizer["Budget_PurposePattern_InvalidRegex", ex.InnerException?.Message ?? ex.Message]);
                return ValidationProblem(ModelState);
            }

            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (DomainValidationException ex)
        {
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create budget rule failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates an existing budget rule.
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
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] BudgetRuleUpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var updated = await _svc.UpdateAsync(id, _current.UserId, req.Amount, req.Interval, req.CustomIntervalMonths, req.StartDate, req.EndDate, req.PurposePattern, req.UseRegex, ct);
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
            // If the exception originates from PurposePattern regex validation, return a ModelState validation error
            if (string.Equals(ex.ParamName, "pattern", StringComparison.OrdinalIgnoreCase) || string.Equals(ex.ParamName, "PurposePattern", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(
                    nameof(BudgetRuleUpdateRequest.PurposePattern),
                    _localizer["Budget_PurposePattern_InvalidRegex", ex.InnerException?.Message ?? ex.Message]);
                return ValidationProblem(ModelState);
            }

            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (DomainValidationException ex)
        {
            return Conflict(ApiErrorFactory.FromDomainValidationException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update budget rule failed {RuleId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a budget rule.
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
            _logger.LogError(ex, "Delete budget rule failed {RuleId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Lists rules for a budget category.
    /// </summary>
    /// <param name="budgetCategoryId">The budget category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("by-category/{budgetCategoryId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<BudgetRuleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByCategoryAsync(Guid budgetCategoryId, CancellationToken ct)
    {
        try
        {
            var list = await _svc.ListByCategoryAsync(_current.UserId, budgetCategoryId, ct);
            return Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List budget rules by category failed {CategoryId}", budgetCategoryId);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
