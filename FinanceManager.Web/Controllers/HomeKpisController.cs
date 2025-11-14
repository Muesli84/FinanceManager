using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// CRUD endpoints for user-scoped Home KPI configurations used on the dashboard.
/// Delegates business logic to <see cref="IHomeKpiService"/>.
/// </summary>
[ApiController]
[Route("api/home-kpis")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class HomeKpisController : ControllerBase
{
    private readonly IHomeKpiService _service;
    private readonly ICurrentUserService _current;
    private readonly ILogger<HomeKpisController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="HomeKpisController"/>.
    /// </summary>
    /// <param name="service">Service managing Home KPI entities.</param>
    /// <param name="current">Current user context service.</param>
    /// <param name="logger">Logger instance.</param>
    public HomeKpisController(IHomeKpiService service, ICurrentUserService current, ILogger<HomeKpisController> logger)
    {
        _service = service; _current = current; _logger = logger;
    }

    /// <summary>
    /// Returns the list of Home KPI configurations for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="HomeKpiDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<HomeKpiDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var list = await _service.ListAsync(_current.UserId, ct);
        return Ok(list);
    }

    /// <summary>
    /// Request payload to create a new Home KPI.
    /// </summary>
    public sealed class CreateRequest
    {
        [Required] public HomeKpiKind Kind { get; set; }
        public Guid? ReportFavoriteId { get; set; }
        public HomeKpiPredefined? PredefinedType { get; set; }
        [MaxLength(120)] public string? Title { get; set; }
        [Required] public HomeKpiDisplayMode DisplayMode { get; set; }
        [Range(0,int.MaxValue)] public int SortOrder { get; set; }
    }

    /// <summary>
    /// Creates a new Home KPI configuration for the current user.
    /// </summary>
    /// <param name="req">Create request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created <see cref="HomeKpiDto"/>, or appropriate error responses.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        try
        {
            var dto = await _service.CreateAsync(_current.UserId, new HomeKpiCreateRequest(req.Kind, req.ReportFavoriteId, req.PredefinedType, req.Title, req.DisplayMode, req.SortOrder), ct);
            return CreatedAtRoute("GetHomeKpi", new { id = dto.Id }, dto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create home kpi failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Request payload to update an existing Home KPI.
    /// </summary>
    public sealed class UpdateRequest
    {
        [Required] public HomeKpiKind Kind { get; set; }
        public Guid? ReportFavoriteId { get; set; }
        public HomeKpiPredefined? PredefinedType { get; set; }
        [MaxLength(120)] public string? Title { get; set; }
        [Required] public HomeKpiDisplayMode DisplayMode { get; set; }
        [Range(0,int.MaxValue)] public int SortOrder { get; set; }
    }

    /// <summary>
    /// Retrieves a specific Home KPI configuration for the current user.
    /// </summary>
    /// <param name="id">Home KPI identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="HomeKpiDto"/> or 404 when not found.</returns>
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
    /// Updates an existing Home KPI configuration.
    /// </summary>
    /// <param name="id">Home KPI identifier.</param>
    /// <param name="req">Update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated DTO, 404 if not found, or error responses for conflicts/validation.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(HomeKpiDto), StatusCodes.Status200OK)]
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
            return Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update home kpi {HomeKpiId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a Home KPI configuration.
    /// </summary>
    /// <param name="id">Home KPI identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or NotFound when missing.</returns>
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
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}
