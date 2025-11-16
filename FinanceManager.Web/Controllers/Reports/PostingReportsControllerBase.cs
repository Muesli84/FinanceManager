using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Mvc;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Web.Controllers.Reports;

/// <summary>
/// Base controller for posting aggregate time series endpoints.
/// Derived controllers only supply route and the <see cref="PostingKind"/> mapping (Kind property).
/// </summary>
public abstract class PostingReportsControllerBase : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPostingTimeSeriesService _seriesService;

    /// <summary>
    /// Creates a new instance of <see cref="PostingReportsControllerBase"/>.
    /// </summary>
    /// <param name="currentUser">Current user service used to determine ownership context.</param>
    /// <param name="seriesService">Service providing aggregated time series data.</param>
    protected PostingReportsControllerBase(ICurrentUserService currentUser, IPostingTimeSeriesService seriesService)
    {
        _currentUser = currentUser;
        _seriesService = seriesService;
    }

    /// <summary>
    /// Posting kind supplied by derived controllers.
    /// </summary>
    protected abstract PostingKind Kind { get; }

    private static int? NormalizeYears(int? maxYearsBack)
    {
        if (!maxYearsBack.HasValue) { return null; }
        return Math.Clamp(maxYearsBack.Value, 1, 10);
    }

    /// <summary>
    /// Shared internal handler used by specific controllers to return aggregates for a single entity.
    /// </summary>
    /// <param name="entityId">Entity identifier to retrieve aggregates for (account/contact/... depending on Kind).</param>
    /// <param name="period">Aggregation period name (Month/Quarter/HalfYear/Year).</param>
    /// <param name="take">Maximum number of points to return.</param>
    /// <param name="maxYearsBack">Optional limit in years for how far back to consider data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult with an ordered list of <see cref="TimeSeriesPointDto"/>, or NotFound when entity not owned by user.</returns>
    protected async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetInternalAsync(Guid entityId, string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        if (!Enum.TryParse<AggregatePeriod>(period, true, out var p))
        {
            p = AggregatePeriod.Month;
        }

        // Apply same defaulting / clamping semantics as service consumer would expect.
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);
        var years = NormalizeYears(maxYearsBack);

        var data = await _seriesService.GetAsync(_currentUser.UserId, Kind, entityId, p, take, years, ct);
        if (data == null)
        {
            return NotFound();
        }
        var result = data.Select(a => new TimeSeriesPointDto(a.PeriodStart, a.Amount)).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Shared internal handler used by derived controllers to return aggregates across all owned entities of the Kind.
    /// </summary>
    /// <param name="period">Aggregation period name.</param>
    /// <param name="take">Maximum number of points to return.</param>
    /// <param name="maxYearsBack">Optional limit in years for how far back to consider data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult with an ordered list of <see cref="TimeSeriesPointDto"/>.</returns>
    protected async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAllInternalAsync(string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        if (!Enum.TryParse<AggregatePeriod>(period, true, out var p))
        {
            p = AggregatePeriod.Month;
        }
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);
        var years = NormalizeYears(maxYearsBack);
        var data = await _seriesService.GetAllAsync(_currentUser.UserId, Kind, p, take, years, ct);
        var result = data.Select(a => new TimeSeriesPointDto(a.PeriodStart, a.Amount)).ToList();
        return Ok(result);
    }
}
