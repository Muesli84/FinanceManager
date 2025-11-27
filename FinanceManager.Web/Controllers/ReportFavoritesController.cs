using FinanceManager.Application;
using FinanceManager.Application.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/report-favorites")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class ReportFavoritesController : ControllerBase
{
    private readonly IReportFavoriteService _favorites;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportFavoritesController> _logger;

    public ReportFavoritesController(IReportFavoriteService favorites, ICurrentUserService current, ILogger<ReportFavoritesController> logger)
    {
        _favorites = favorites;
        _current = current;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReportFavoriteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        var list = await _favorites.ListAsync(_current.UserId, ct);
        return Ok(list);
    }

    [HttpGet("{id:guid}", Name = "GetReportFavorite")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _favorites.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] ReportFavoriteCreateApiRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(req.Filters.AccountIds, req.Filters.ContactIds, req.Filters.SavingsPlanIds, req.Filters.SecurityIds, req.Filters.ContactCategoryIds, req.Filters.SavingsPlanCategoryIds, req.Filters.SecurityCategoryIds, req.Filters.SecuritySubTypes, req.Filters.IncludeDividendRelated);
            var dto = await _favorites.CreateAsync(
                _current.UserId,
                new ReportFavoriteCreateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, (ReportInterval)req.Interval, req.Take, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable, req.PostingKinds, filters, req.UseValutaDate),
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

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] ReportFavoriteUpdateApiRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(req.Filters.AccountIds, req.Filters.ContactIds, req.Filters.SavingsPlanIds, req.Filters.SecurityIds, req.Filters.ContactCategoryIds, req.Filters.SavingsPlanCategoryIds, req.Filters.SecurityCategoryIds, req.Filters.SecuritySubTypes, req.Filters.IncludeDividendRelated);
            var dto = await _favorites.UpdateAsync(
                id,
                _current.UserId,
                new ReportFavoriteUpdateRequest(req.Name.Trim(), req.PostingKind, req.IncludeCategory, (ReportInterval)req.Interval, req.Take, req.ComparePrevious, req.CompareYear, req.ShowChart, req.Expandable, req.PostingKinds, filters, req.UseValutaDate),
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
