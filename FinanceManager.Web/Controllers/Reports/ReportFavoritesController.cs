using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports; // added for ReportInterval
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Web.Controllers.Reports; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// CRUD operations for user-scoped report favorites (saved report configurations).
/// Delegates business logic to <see cref="IReportFavoriteService"/>.
/// </summary>
[ApiController]
[Route("api/report-favorites")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReportFavoritesController : ControllerBase
{
    private readonly IReportFavoriteService _favorites;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportFavoritesController> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="ReportFavoritesController"/>.
    /// </summary>
    /// <param name="favorites">Service handling report favorite operations.</param>
    /// <param name="current">Current user context service.</param>
    /// <param name="logger">Logger instance.</param>
    public ReportFavoritesController(IReportFavoriteService favorites, ICurrentUserService current, ILogger<ReportFavoritesController> logger)
    {
        _favorites = favorites;
        _current = current;
        _logger = logger;
    }

    /// <summary>
    /// Filter DTO used inside create/update payloads to restrict reports to specific entities/categories.
    /// </summary>
    public sealed class FiltersDto
    {
        public IReadOnlyCollection<Guid>? AccountIds { get; set; }
        public IReadOnlyCollection<Guid>? ContactIds { get; set; }
        public IReadOnlyCollection<Guid>? SavingsPlanIds { get; set; }
        public IReadOnlyCollection<Guid>? SecurityIds { get; set; }
        public IReadOnlyCollection<Guid>? ContactCategoryIds { get; set; }
        public IReadOnlyCollection<Guid>? SavingsPlanCategoryIds { get; set; }
        public IReadOnlyCollection<Guid>? SecurityCategoryIds { get; set; }
        public IReadOnlyCollection<int>? SecuritySubTypes { get; set; }
        public bool? IncludeDividendRelated { get; set; }
    }

    /// <summary>
    /// Request payload to create a new report favorite.
    /// </summary>
    public sealed class CreateRequest
    {
        [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
        [Range(0, 10)] public int PostingKind { get; set; }
        public bool IncludeCategory { get; set; }
        [Required] public ReportInterval Interval { get; set; }
        [Range(1,120)] public int Take { get; set; } = 24;
        public bool ComparePrevious { get; set; }
        public bool CompareYear { get; set; }
        public bool ShowChart { get; set; }
        public bool Expandable { get; set; } = true;
        public IReadOnlyCollection<int>? PostingKinds { get; set; }
        public FiltersDto? Filters { get; set; }
        public bool UseValutaDate { get; set; }
    }

    /// <summary>
    /// Returns the list of report favorites for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="ReportFavoriteDto"/>.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReportFavoriteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var list = await _favorites.ListAsync(_current.UserId, ct);
        return Ok(list);
    }

    /// <summary>
    /// Gets a specific report favorite by id for the current user.
    /// </summary>
    /// <param name="id">Favorite identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the DTO or 404 when not found.</returns>
    [HttpGet("{id:guid}", Name = "GetReportFavorite")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _favorites.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Creates a new report favorite for the current user.
    /// </summary>
    /// <param name="req">Create request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created DTO or error responses.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(req.Filters.AccountIds, req.Filters.ContactIds, req.Filters.SavingsPlanIds, req.Filters.SecurityIds, req.Filters.ContactCategoryIds, req.Filters.SavingsPlanCategoryIds, req.Filters.SecurityCategoryIds, req.Filters.SecuritySubTypes, req.Filters.IncludeDividendRelated);
            var dto = await _favorites.CreateAsync(
                _current.UserId,
                new ReportFavoriteCreateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, req.Interval, req.Take, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable, req.PostingKinds, filters, req.UseValutaDate),
                ct);
            return CreatedAtRoute("GetReportFavorite", new { id = dto.Id }, dto);
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
            _logger.LogError(ex, "Create report favorite failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Request payload to update an existing report favorite.
    /// </summary>
    public sealed class UpdateRequest
    {
        [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
        [Range(0, 10)] public int PostingKind { get; set; }
        public bool IncludeCategory { get; set; }
        [Required] public ReportInterval Interval { get; set; }
        [Range(1,120)] public int Take { get; set; } = 24;
        public bool ComparePrevious { get; set; }
        public bool CompareYear { get; set; }
        public bool ShowChart { get; set; }
        public bool Expandable { get; set; } = true;
        public IReadOnlyCollection<int>? PostingKinds { get; set; }
        public FiltersDto? Filters { get; set; }
        public bool UseValutaDate { get; set; }
    }

    /// <summary>
    /// Updates an existing report favorite.
    /// </summary>
    /// <param name="id">Favorite identifier.</param>
    /// <param name="req">Update request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated DTO or 404 when not found.</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(req.Filters.AccountIds, req.Filters.ContactIds, req.Filters.SavingsPlanIds, req.Filters.SecurityIds, req.Filters.ContactCategoryIds, req.Filters.SavingsPlanCategoryIds, req.Filters.SecurityCategoryIds, req.Filters.SecuritySubTypes, req.Filters.IncludeDividendRelated);
            var dto = await _favorites.UpdateAsync(
                id,
                _current.UserId,
                new ReportFavoriteUpdateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, req.Interval, req.Take, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable, req.PostingKinds, filters, req.UseValutaDate),
                ct);
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
            _logger.LogError(ex, "Update report favorite {FavoriteId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Deletes a report favorite owned by the current user.
    /// </summary>
    /// <param name="id">Favorite identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>NoContent on success or NotFound when missing.</returns>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _favorites.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete report favorite {FavoriteId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}

