using FinanceManager.Application;
using FinanceManager.Application.Budget;
using FinanceManager.Shared.Dtos.Budget;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;
using FinanceManager.Infrastructure;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Shared.Dtos.Postings;
using FinanceManager.Web.Infrastructure;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides budget reporting endpoints.
/// </summary>
[ApiController]
[Route("api/budget/report")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class BudgetReportsController : ControllerBase
{
    private const string Origin = "API_BudgetReport";

    private readonly IBudgetReportService _reports;
    private readonly ICurrentUserService _current;
    private readonly ILogger<BudgetReportsController> _logger;
    private readonly IStringLocalizer<Controller> _localizer;
    private readonly AppDbContext _db;
    private readonly IBudgetReportExportService _export;
    private readonly IReportCacheService _cacheService;

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    /// <param name="reports">The reports.</param>
    /// <param name="db">The db.</param>
    /// <param name="current">The current.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">The localizer.</param>
    /// <param name="export">The export.</param>
    /// <param name="cacheService">The cache service.</param>
    public BudgetReportsController(
        IBudgetReportService reports,
        AppDbContext db,
        ICurrentUserService current,
        ILogger<BudgetReportsController> logger,
        IStringLocalizer<Controller> localizer,
        IBudgetReportExportService export,
        IReportCacheService cacheService)
    {
        _reports = reports;
        _db = db;
        _current = current;
        _logger = logger;
        _localizer = localizer;
        _export = export;
        _cacheService = cacheService;
    }

    /// <summary>
    /// Exports all postings of the budget report for the total report range as an Excel file.
    /// </summary>
    /// <param name="asOf">The as of.</param>
    /// <param name="months">The months.</param>
    /// <param name="dateBasis">The date basis.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync([FromQuery] DateOnly asOf, [FromQuery] int months = 12, [FromQuery] BudgetReportDateBasis dateBasis = BudgetReportDateBasis.BookingDate, CancellationToken ct = default)
    {
        try
        {
            var req = new BudgetReportExportRequest(asOf, months, dateBasis);
            var (contentType, fileName, content) = await _export.GenerateXlsxAsync(_current.UserId, req, ct);
            return new StreamCallbackResult(contentType, async (output, token) =>
            {
                await using (content)
                {
                    await content.CopyToAsync(output, token);
                }
            })
            {
                FileDownloadName = fileName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Budget report export failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Clears cached report data for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> indicating success.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpPost("cache/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetCacheAsync(CancellationToken ct = default)
    {
        await _cacheService.ClearReportCacheAsync(_current.UserId, ct);
        return NoContent();
    }

    /// <summary>
    /// Generates a budget report for the current user.
    /// </summary>
    /// <param name="dateBasis">Whether to base the report on booking date or valuta date.</param>
    /// <param name="date">The date for which the KPI should be calculated. If not provided, the current date is used.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Returns planned/actual income and expenses for the Home Monthly Budget KPI.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("kpi-monthly")]
    [ProducesResponseType(typeof(MonthlyBudgetKpiDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMonthlyKpiAsync([FromQuery] DateOnly? date = null, [FromQuery] BudgetReportDateBasis dateBasis = BudgetReportDateBasis.ValutaDate, CancellationToken ct = default)
    {
        try
        {
            var kpi = await _reports.GetMonthlyKpiAsync(_current.UserId, date, dateBasis, ct);
            return Ok(kpi);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Get monthly budget KPI request invalid");
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get monthly budget KPI failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
    /// <summary>
    /// Generates a budget report for the current user.
    /// </summary>
    /// <param name="req">The report request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing the generated <see cref="BudgetReportDto"/> or a validation/error result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(BudgetReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync([FromBody] BudgetReportRequest req, CancellationToken ct = default)
    {
        try
        {
            if (req.Months < 1 || req.Months > 60)
            {
                var ex = new ArgumentOutOfRangeException(nameof(req.Months), "Months must be 1..60");
                return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
            }

            // Aggregation (categories/purposes/subtotals/unbudgeted/cost-neutral/totals and the period
            // table) is entirely delegated to Budgetbericht.GetCurrentResult()/GetCumulativeResult() via
            // IBudgetReportService.GetReportAsync, so there is a single implementation of this logic shared
            // with the KPI tile (BudgetReportService.GetMonthlyKpiAsync) rather than a second, parallel one
            // here. This always recomputes fresh (bypasses the raw-data cache), matching the previous
            // behavior of this endpoint.
            var result = await _reports.GetReportAsync(
                _current.UserId,
                req.AsOfDate,
                req.Months,
                req.Interval,
                req.CategoryValueScope,
                req.DateBasis,
                ct);

            return Ok(result);
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
            _logger.LogError(ex, "Get budget report failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Returns raw budget report data for UI scenarios that need posting-level budget valuation state.
    /// </summary>
    /// <param name="req">The report request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing raw budget report data.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost("raw")]
    [ProducesResponseType(typeof(BudgetReportRawDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRawAsync([FromBody] BudgetReportRequest req, CancellationToken ct = default)
    {
        try
        {
            if (req.Months < 1 || req.Months > 60)
            {
                var ex = new ArgumentOutOfRangeException(nameof(req.Months), "Months must be 1..60");
                return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
            }

            var to = new DateOnly(req.AsOfDate.Year, req.AsOfDate.Month, DateTime.DaysInMonth(req.AsOfDate.Year, req.AsOfDate.Month));
            var from = new DateOnly(to.Year, to.Month, 1).AddMonths(-(req.Months - 1));
            var raw = await _reports.GetRawDataAsync(_current.UserId, from, to, req.DateBasis, ct);
            return Ok(raw);
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
            _logger.LogError(ex, "Get raw budget report failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Lists postings that are not covered by any budget purpose for the given date range.
    /// </summary>
    /// <param name="from">The from.</param>
    /// <param name="to">The to.</param>
    /// <param name="dateBasis">The date basis.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("unbudgeted")]
    [ProducesResponseType(typeof(IReadOnlyList<PostingServiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnbudgetedAsync(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] BudgetReportDateBasis dateBasis = BudgetReportDateBasis.BookingDate,
        [FromQuery] string? kind = null,
        CancellationToken ct = default)
    {
        try
        {
            // Determine range boundaries
            var fromDt = (from ?? DateTime.MinValue).Date;
            var toDt = (to ?? DateTime.MaxValue);

            var ownerUserId = _current.UserId;
            var fromDate = DateOnly.FromDateTime(fromDt);
            var toDate = DateOnly.FromDateTime(toDt);

            // Use GetRawDataAsync to get properly filtered posting IDs that respect pattern matching.
            var raw = await _reports.GetRawDataAsync(ownerUserId, fromDate, toDate, dateBasis, ct);

            var unbudgetedPostingIds = (raw.UnbudgetedPostings ?? Array.Empty<BudgetReportPostingRawDataDto>())
                .Select(p => p.PostingId)
                .ToHashSet();

            if (unbudgetedPostingIds.Count == 0)
            {
                return Ok(new List<PostingServiceDto>());
            }

            // Load full posting details for mapping
            var allPostingsQuery = _db.Postings.AsNoTracking();
            allPostingsQuery = dateBasis == BudgetReportDateBasis.ValutaDate
                ? allPostingsQuery.Where(p => p.ValutaDate >= fromDt && p.ValutaDate <= toDt)
                : allPostingsQuery.Where(p => p.BookingDate >= fromDt && p.BookingDate <= toDt);

            var postings = await allPostingsQuery
                .Where(p => unbudgetedPostingIds.Contains(p.Id))
                .OrderByDescending(p => dateBasis == BudgetReportDateBasis.ValutaDate ? p.ValutaDate : p.BookingDate)
                .Take(5000)
                .ToListAsync(ct);

            // Optional split for UI: cost-neutral self-contact mirror postings vs remaining.
            // "selfCostNeutral": self-contact, grouped postings (GroupId != empty)
            // "remaining": everything else
            if (!string.IsNullOrWhiteSpace(kind))
            {
                var selfId = await _db.Contacts.AsNoTracking()
                    .Where(c => c.OwnerUserId == ownerUserId && c.Type == FinanceManager.Shared.Dtos.Contacts.ContactType.Self)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefaultAsync(ct);

                if (kind.Equals("selfCostNeutral", StringComparison.OrdinalIgnoreCase))
                {
                    if (selfId.HasValue)
                    {
                        postings = postings
                            .Where(p => p.ContactId == selfId.Value && p.GroupId != Guid.Empty)
                            .ToList();
                    }
                    else
                    {
                        postings.Clear();
                    }
                }
                else if (kind.Equals("remaining", StringComparison.OrdinalIgnoreCase))
                {
                    if (selfId.HasValue)
                    {
                        postings = postings
                            .Where(p => p.ContactId != selfId.Value || p.GroupId == Guid.Empty)
                            .ToList();
                    }
                }
            }

            var unbudgeted = postings
                .Select(p => new PostingServiceDto(
                    p.Id,
                    p.BookingDate,
                    p.ValutaDate,
                    p.Amount,
                    p.Kind,
                    p.AccountId,
                    p.ContactId,
                    p.SavingsPlanId,
                    p.SecurityId,
                    p.SourceId,
                    p.Subject,
                    p.RecipientName,
                    p.Description,
                    p.SecuritySubType,
                    p.Quantity,
                    p.GroupId,
                    p.LinkedPostingId,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    p.IsReversed,
                    p.IsReversal,
                    p.ReversedByPostingId,
                    p.ReversalForPostingId))
                .ToList();

            return Ok(unbudgeted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Get unbudgeted postings failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}
