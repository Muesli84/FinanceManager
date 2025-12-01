using FinanceManager.Application;
using FinanceManager.Application.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportAggregationService _agg;
    private readonly IReportFavoriteService _favorites;
    private readonly ICurrentUserService _current;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        IReportAggregationService agg,
        IReportFavoriteService favorites,
        ICurrentUserService current,
        ILogger<ReportsController> logger)
    {
        _agg = agg;
        _favorites = favorites;
        _current = current;
        _logger = logger;
    }

    // ------------------------------------------------------------
    // Aggregates
    // POST api/report-aggregates
    [HttpPost("report-aggregates")]
    [ProducesResponseType(typeof(ReportAggregationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueryAsync([FromBody] ReportAggregatesQueryRequest req, CancellationToken ct)
    {
        try
        {
            if (req.Take < 1 || req.Take > 200)
            {
                return BadRequest(new { error = "Take must be 1..200" });
            }

            // Optional multi-kind fallback via query string (?postingKinds=0,1,2)
            IReadOnlyCollection<PostingKind>? multi = req.PostingKinds;
            if (multi == null || multi.Count == 0)
            {
                var kindsParam = Request.Query["postingKinds"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(kindsParam))
                {
                    multi = kindsParam
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
                        .Where(v => v.HasValue)
                        .Select(v => (PostingKind)v!.Value)
                        .Distinct()
                        .ToArray();
                }
            }

            // Optional analysis date via query
            DateTime? analysisDate = req.AnalysisDate;
            if (!analysisDate.HasValue)
            {
                var adStr = Request.Query["analysisDate"].FirstOrDefault();
                if (DateTime.TryParse(adStr, out var ad))
                {
                    analysisDate = new DateTime(ad.Year, ad.Month, 1);
                }
            }
            else
            {
                var d = analysisDate.Value.Date;
                analysisDate = new DateTime(d.Year, d.Month, 1);
            }

            ReportAggregationFilters? filters = null;
            if (req.Filters != null)
            {
                filters = new ReportAggregationFilters(
                    req.Filters.AccountIds,
                    req.Filters.ContactIds,
                    req.Filters.SavingsPlanIds,
                    req.Filters.SecurityIds,
                    req.Filters.ContactCategoryIds,
                    req.Filters.SavingsPlanCategoryIds,
                    req.Filters.SecurityCategoryIds,
                    req.Filters.SecuritySubTypes,
                    req.Filters.IncludeDividendRelated);
            }

            var query = new ReportAggregationQuery(
                _current.UserId,
                req.PostingKind,
                req.Interval,
                req.Take,
                req.IncludeCategory,
                req.ComparePrevious,
                req.CompareYear,
                multi,
                analysisDate,
                req.UseValutaDate,
                filters);

            var result = await _agg.QueryAsync(query, ct);
            return Ok(result);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("SecuritySubType", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Aggregation failed due to missing column. Retrying.");
            try
            {
                var query = new ReportAggregationQuery(
                    _current.UserId,
                    req.PostingKind,
                    req.Interval,
                    req.Take,
                    req.IncludeCategory,
                    req.ComparePrevious,
                    req.CompareYear,
                    req.PostingKinds,
                    req.AnalysisDate,
                    req.UseValutaDate,
                    req.Filters == null ? null : new ReportAggregationFilters(
                        req.Filters.AccountIds,
                        req.Filters.ContactIds,
                        req.Filters.SavingsPlanIds,
                        req.Filters.SecurityIds,
                        req.Filters.ContactCategoryIds,
                        req.Filters.SavingsPlanCategoryIds,
                        req.Filters.SecurityCategoryIds,
                        req.Filters.SecuritySubTypes,
                        req.Filters.IncludeDividendRelated));
                var result = await _agg.QueryAsync(query, ct);
                return Ok(result);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "Aggregation retry failed");
                return Problem("Unexpected error", statusCode: 500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Report aggregation failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // ------------------------------------------------------------
    // Favorites

    // GET api/report-favorites
    [HttpGet("report-favorites")]
    [ProducesResponseType(typeof(IReadOnlyList<ReportFavoriteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListFavoritesAsync(CancellationToken ct)
        => Ok(await _favorites.ListAsync(_current.UserId, ct));

    // GET api/report-favorites/{id}
    [HttpGet("report-favorites/{id:guid}", Name = "GetReportFavorite")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFavoriteAsync(Guid id, CancellationToken ct)
    {
        var dto = await _favorites.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    // POST api/report-favorites
    [HttpPost("report-favorites")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateFavoriteAsync([FromBody] ReportFavoriteCreateApiRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(
                req.Filters.AccountIds,
                req.Filters.ContactIds,
                req.Filters.SavingsPlanIds,
                req.Filters.SecurityIds,
                req.Filters.ContactCategoryIds,
                req.Filters.SavingsPlanCategoryIds,
                req.Filters.SecurityCategoryIds,
                req.Filters.SecuritySubTypes,
                req.Filters.IncludeDividendRelated);

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

    // PUT api/report-favorites/{id}
    [HttpPut("report-favorites/{id:guid}")]
    [ProducesResponseType(typeof(ReportFavoriteDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateFavoriteAsync(Guid id, [FromBody] ReportFavoriteUpdateApiRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var filters = req.Filters == null ? null : new ReportFavoriteFiltersDto(
                req.Filters.AccountIds,
                req.Filters.ContactIds,
                req.Filters.SavingsPlanIds,
                req.Filters.SecurityIds,
                req.Filters.ContactCategoryIds,
                req.Filters.SavingsPlanCategoryIds,
                req.Filters.SecurityCategoryIds,
                req.Filters.SecuritySubTypes,
                req.Filters.IncludeDividendRelated);

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

    // DELETE api/report-favorites/{id}
    [HttpDelete("report-favorites/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFavoriteAsync(Guid id, CancellationToken ct)
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
