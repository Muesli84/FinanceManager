using FinanceManager.Application;
using FinanceManager.Application.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

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
    private readonly IHomeKpiService _service;
    private readonly ICurrentUserService _current;
    private readonly ILogger<HomeKpisController> _logger;

    public HomeKpisController(IHomeKpiService service, ICurrentUserService current, ILogger<HomeKpisController> logger)
    { _service = service; _current = current; _logger = logger; }

    /// <summary>
    /// Lists all KPI widgets configured by the current user (order determined by <c>SortOrder</c> client-side).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
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
        /// <summary>Optional report favorite id if <see cref="HomeKpiKind.Report"/>.</summary>
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
    [HttpPost]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        try
        {
            var dto = await _service.CreateAsync(_current.UserId, new HomeKpiCreateRequest(req.Kind, req.ReportFavoriteId, req.PredefinedType, req.Title, req.DisplayMode, req.SortOrder), ct);
            return CreatedAtRoute("GetHomeKpi", new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex) { return Conflict(new ApiErrorDto(ex.Message)); }
        catch (ArgumentException ex) { return BadRequest(new ApiErrorDto(ex.Message)); }
        catch (Exception ex) { _logger.LogError(ex, "Create home kpi failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Request payload for updating an existing KPI widget.
    /// </summary>
    public sealed class UpdateRequest
    {
        [Required] public HomeKpiKind Kind { get; set; }
        public Guid? ReportFavoriteId { get; set; }
        public HomeKpiPredefined? PredefinedType { get; set; }
        [MaxLength(120)] public string? Title { get; set; }
        [Required] public HomeKpiDisplayMode DisplayMode { get; set; }
        [Range(0, int.MaxValue)] public int SortOrder { get; set; }
    }

    /// <summary>
    /// Retrieves a single KPI widget by id.
    /// </summary>
    /// <param name="id">KPI id.</param>
    /// <param name="ct">Cancellation token.</param>
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
        catch (InvalidOperationException ex) { return Conflict(new ApiErrorDto(ex.Message)); }
        catch (ArgumentException ex) { return BadRequest(new ApiErrorDto(ex.Message)); }
        catch (Exception ex) { _logger.LogError(ex, "Update home kpi {HomeKpiId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Deletes a KPI widget owned by the current user.
    /// </summary>
    /// <param name="id">KPI id.</param>
    /// <param name="ct">Cancellation token.</param>
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
        catch (Exception ex) { _logger.LogError(ex, "Delete home kpi {HomeKpiId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }
}
