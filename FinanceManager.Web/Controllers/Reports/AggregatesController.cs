using FinanceManager.Application.Reports;
using FinanceManager.Application;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Reports;

/// <summary>
/// Centralized controller that exposes aggregate time series endpoints for multiple resource types.
/// This controller is thin: it validates route/parameters, maps the resource to a <see cref="PostingKind"/>
/// and delegates the actual aggregation work to <see cref="IPostingTimeSeriesService"/>.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api")]
public sealed class AggregatesController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly IPostingTimeSeriesService _seriesService;

    /// <summary>
    /// Creates a new instance of <see cref="AggregatesController"/>.
    /// </summary>
    public AggregatesController(ICurrentUserService currentUser, IPostingTimeSeriesService seriesService)
    {
        _currentUser = currentUser;
        _seriesService = seriesService;
    }

    /// <summary>
    /// Returns aggregated time series for a single entity (account/contact/savings-plan/security).
    /// Route example: GET /api/accounts/{id}/aggregates
    /// </summary>
    [HttpGet("{resource}/{id:guid}/aggregates")]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetForEntityAsync(string resource, Guid id, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
    {
        if (!TryMapResource(resource, out var kind))
        {
            return BadRequest("Unknown resource");
        }

        if (!Enum.TryParse<AggregatePeriod>(period ?? "Month", true, out var p)) p = AggregatePeriod.Month;
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);
        var years = NormalizeYears(maxYearsBack);

        var data = await _seriesService.GetAsync(_currentUser.UserId, kind, id, p, take, years, ct);
        if (data == null) return NotFound();
        var result = data.Select(a => new TimeSeriesPointDto(a.PeriodStart, a.Amount)).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Returns aggregated time series across all owned entities of the given resource type.
    /// Route example: GET /api/accounts/aggregates
    /// </summary>
    [HttpGet("{resource}/aggregates")]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAllForResourceAsync(string resource, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
    {
        if (!TryMapResource(resource, out var kind))
        {
            return BadRequest("Unknown resource");
        }

        if (!Enum.TryParse<AggregatePeriod>(period ?? "Month", true, out var p)) p = AggregatePeriod.Month;
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);
        var years = NormalizeYears(maxYearsBack);

        var data = await _seriesService.GetAllAsync(_currentUser.UserId, kind, p, take, years, ct);
        var result = data.Select(a => new TimeSeriesPointDto(a.PeriodStart, a.Amount)).ToList();
        return Ok(result);
    }

    /// <summary>
    /// Special endpoint for security dividends (keeps compatibility with existing route).
    /// </summary>
    [HttpGet("securities/dividends")]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetSecuritiesDividendsAsync([FromQuery] string? period = null, [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
    {
        if (!Enum.TryParse<AggregatePeriod>(period ?? "Quarter", true, out var p)) p = AggregatePeriod.Quarter;
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10) : take, 1, 200);
        var years = NormalizeYears(maxYearsBack);

        var data = await _seriesService.GetDividendsAsync(_currentUser.UserId, p, take, years, ct);
        var result = (data ?? Array.Empty<FinanceManager.Application.Reports.AggregatePointDto>()).Select(a => new TimeSeriesPointDto(a.PeriodStart, a.Amount)).ToList();
        return Ok(result);
    }

    private static int? NormalizeYears(int? maxYearsBack)
    {
        if (!maxYearsBack.HasValue) return null;
        return Math.Clamp(maxYearsBack.Value, 1, 10);
    }

    private static bool TryMapResource(string resource, out PostingKind kind)
    {
        kind = default;
        if (string.IsNullOrWhiteSpace(resource)) return false;
        resource = resource.Trim().ToLowerInvariant();
        return resource switch
        {
            "accounts" => SetKind(PostingKind.Bank, out kind),
            "contacts" => SetKind(PostingKind.Contact, out kind),
            "savings-plans" => SetKind(PostingKind.SavingsPlan, out kind),
            "securities" => SetKind(PostingKind.Security, out kind),
            _ => false
        };

        static bool SetKind(PostingKind k, out PostingKind dest) { dest = k; return true; }
    }
}
