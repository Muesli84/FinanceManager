using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Base controller for posting aggregate time series endpoints.
/// Derived controllers only supply route + PostingKind mapping.
/// </summary>
public abstract class PostingReportsControllerBase : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPostingTimeSeriesService _seriesService;

    protected PostingReportsControllerBase(ICurrentUserService currentUser, IPostingTimeSeriesService seriesService)
    {
        _currentUser = currentUser;
        _seriesService = seriesService;
    }

    protected abstract PostingKind Kind { get; }

    public sealed record TimeSeriesPointDto(DateTime PeriodStart, decimal Amount);

    private static int? NormalizeYears(int? maxYearsBack)
    {
        if (!maxYearsBack.HasValue) { return null; }
        return Math.Clamp(maxYearsBack.Value, 1, 10);
    }

    /// <summary>
    /// Shared internal handler used by specific controllers.
    /// </summary>
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
