using FinanceManager.Domain.Postings;
using FinanceManager.Application;
using FinanceManager.Application.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Reports;

/// <summary>
/// Provides quarterly aggregated dividend amounts for securities owned by the current user.
/// Delegates aggregation logic to <see cref="IPostingTimeSeriesService"/>.
/// </summary>
[ApiController]
[Route("api/securities/dividends")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DividendsReportsController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPostingTimeSeriesService _seriesService;

    /// <summary>
    /// Creates a new <see cref="DividendsReportsController"/>.
    /// </summary>
    public DividendsReportsController(ICurrentUserService currentUser, IPostingTimeSeriesService seriesService)
    {
        _currentUser = currentUser;
        _seriesService = seriesService;
    }

    /// <summary>
    /// DTO representing a single time series point (period start and aggregated amount).
    /// </summary>
    public sealed record TimeSeriesPointDto(DateTime PeriodStart, decimal Amount);

    /// <summary>
    /// Returns aggregated dividend amounts for securities owned by the current user.
    /// This controller delegates the aggregation to <see cref="IPostingTimeSeriesService.GetDividendsAsync"/>.
    /// </summary>
    /// <param name="period">Aggregation period name (ignored by service implementation but accepted for compatibility).</param>
    /// <param name="take">Maximum number of points to return.</param>
    /// <param name="maxYearsBack">Optional limit for how many years back to include.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync([FromQuery] string? period = null, [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
    {
        // Map period string to AggregatePeriod; default to Quarter if not parseable
        if (!Enum.TryParse<AggregatePeriod>(period ?? "Quarter", true, out var p)) p = AggregatePeriod.Quarter;

        var points = await _seriesService.GetDividendsAsync(_currentUser.UserId, p, take, maxYearsBack, ct);
        if (points == null) points = Array.Empty<FinanceManager.Application.Reports.AggregatePointDto>();
        var dto = points.Select(x => new TimeSeriesPointDto(x.PeriodStart, x.Amount)).ToList();
        return Ok(dto);
    }
}

