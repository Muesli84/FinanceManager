using FinanceManager.Application;
using FinanceManager.Application.Common;
using FinanceManager.Application.Reports;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Manages Home KPI widgets for the signed-in user: list, create, update, delete and retrieve single entries.
/// Supports predefined KPIs and custom report favorite based KPIs.
/// </summary>
[ApiController]
[Route("api/home-kpis")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class HomeKpisController : ControllerBase
{
    private const string Origin = "API_HomeKpi";

    private readonly IHomeKpiService _service;
    private readonly ICurrentUserService _current;
    private readonly ILogger<HomeKpisController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Initializes a new instance of <see cref="HomeKpisController"/>.
    /// </summary>
    /// <param name="service">Service implementing KPI management usecases.</param>
    /// <param name="current">Service providing current user context.</param>
    /// <param name="logger">Logger used to record unexpected errors and diagnostics.</param>
    /// <param name="localizer">Localizer for API error messages.</param>
    public HomeKpisController(
        IHomeKpiService service,
        ICurrentUserService current,
        ILogger<HomeKpisController> logger,
        IStringLocalizer<Controller> localizer)
    {
        _service = service;
        _current = current;
        _logger = logger;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists all KPI widgets configured by the current user (order determined by <c>SortOrder</c> client-side).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Returns 200 OK with a read-only list of <see cref="HomeKpiDto"/> instances for the current user.
    /// </returns>
    /// <exception cref="HttpRequestException">If the underlying service call fails.</exception>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<HomeKpiDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var list = await _service.ListAsync(_current.UserId, ct);
        return Ok(list);
    }

    /// <summary>
    /// Request payload for creating a new Home KPI widget.
    /// </summary>
    public sealed class CreateRequest
    {
        /// <summary>Widget kind definition.</summary>
        [Required] public HomeKpiKind Kind { get; set; }
        /// <summary>Optional report favorite id if <see cref="HomeKpiKind.ReportFavorite"/>.</summary>
        public Guid? ReportFavoriteId { get; set; }
        /// <summary>Optional predefined KPI type if <see cref="HomeKpiKind.Predefined"/>.</summary>
        public HomeKpiPredefined? PredefinedType { get; set; }
        /// <summary>Optional custom title (max 120 chars).</summary>
        [MaxLength(120)] public string? Title { get; set; }
        /// <summary>Display mode (e.g. Compact / Expanded).</summary>
        [Required] public HomeKpiDisplayMode DisplayMode { get; set; }
        /// <summary>Sorting order (ascending).</summary>
        [Range(0, int.MaxValue)] public int SortOrder { get; set; }
    }

    /// <summary>
    /// Creates a new KPI widget for the current user.
    /// </summary>
    /// <param name="req">Create request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 201 Created with the created <see cref="HomeKpiDto"/> when successful;
    /// 400 Bad Request when the request is invalid;
    /// 409 Conflict when the requested linkage is invalid or already exists.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the service detects invalid arguments (mapped to 400).</exception>
    /// <exception cref="InvalidOperationException">Thrown when the service detects a conflict (mapped to 409).</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var dto = await _service.CreateAsync(_current.UserId, new HomeKpiCreateRequest(req.Kind, req.ReportFavoriteId, req.PredefinedType, req.Title, req.DisplayMode, req.SortOrder), ct);
            return CreatedAtRoute("GetHomeKpi", new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            const string code = "Err_Conflict_HomeKpi";
            var entry = _localizer[$"{Origin}_{code}"];
            var message = entry.ResourceNotFound ? ex.Message : entry.Value;
            return Conflict(ApiErrorDto.Create(Origin, code, message));
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
            _logger.LogError(ex, "Create home kpi failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Request payload for updating an existing KPI widget.
    /// </summary>
    public sealed class UpdateRequest
    {
        /// <summary>Widget kind definition.</summary>
        [Required] public HomeKpiKind Kind { get; set; }
        /// <summary>Optional report favorite id if <see cref="HomeKpiKind.ReportFavorite"/>.</summary>
        public Guid? ReportFavoriteId { get; set; }
        /// <summary>Optional predefined KPI type if <see cref="HomeKpiKind.Predefined"/>.</summary>
        public HomeKpiPredefined? PredefinedType { get; set; }
        /// <summary>Optional custom title (max 120 chars).</summary>
        [MaxLength(120)] public string? Title { get; set; }
        /// <summary>Display mode (e.g. Compact / Expanded).</summary>
        [Required] public HomeKpiDisplayMode DisplayMode { get; set; }
        /// <summary>Sorting order (ascending).</summary>
        [Range(0, int.MaxValue)] public int SortOrder { get; set; }
    }

    /// <summary>
    /// Retrieves a single KPI widget by id.
    /// </summary>
    /// <param name="id">KPI id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with the <see cref="HomeKpiDto"/> when found; 404 Not Found when the KPI does not exist or does not belong to the current user.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}", Name = "GetHomeKpi")]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var list = await _service.ListAsync(_current.UserId, ct);
        var item = list.FirstOrDefault(k => k.Id == id);
        return item == null ? NotFound() : Ok(item);
    }

    /// <summary>
    /// Updates a KPI widget (kind, display settings, linkage, sort order).
    /// </summary>
    /// <param name="id">KPI id.</param>
    /// <param name="req">Update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with the updated <see cref="HomeKpiDto"/> when the update succeeds;
    /// 400 Bad Request when input is invalid;
    /// 404 Not Found when the KPI does not exist;
    /// 409 Conflict when the requested update cannot be applied.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid (mapped to 400).</exception>
    /// <exception cref="InvalidOperationException">Thrown when a conflict occurs (mapped to 409).</exception>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    /// <response code="409">The HTTP 409 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        try
        {
            var dto = await _service.UpdateAsync(id, _current.UserId, new HomeKpiUpdateRequest(req.Kind, req.ReportFavoriteId, req.PredefinedType, req.Title, req.DisplayMode, req.SortOrder), ct);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (InvalidOperationException ex)
        {
            const string code = "Err_Conflict_HomeKpi";
            var entry = _localizer[$"{Origin}_{code}"];
            var message = entry.ResourceNotFound ? ex.Message : entry.Value;
            return Conflict(ApiErrorDto.Create(Origin, code, message));
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
            _logger.LogError(ex, "Update home kpi {HomeKpiId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Deletes a KPI widget owned by the current user.
    /// </summary>
    /// <param name="id">KPI id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 204 No Content when deletion succeeded; 404 Not Found when the KPI does not exist.
    /// </returns>
    /// <exception cref="HttpRequestException">Thrown when the underlying service call fails.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _service.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete home kpi {HomeKpiId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
