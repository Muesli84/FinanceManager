using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Reports; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Controller providing aggregate time series for a single savings plan (savings plan postings).
/// Delegates the actual work to <see cref="PostingReportsControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/savings-plans/{planId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlanReportsController : PostingReportsControllerBase
{
    /// <summary>
    /// The posting kind this controller exposes (SavingsPlan postings).
    /// </summary>
    protected override PostingKind Kind => PostingKind.SavingsPlan;

    /// <summary>
    /// Creates a new instance of <see cref="SavingsPlanReportsController"/>.
    /// </summary>
    /// <param name="current">Current user service.</param>
    /// <param name="series">Posting time series service.</param>
    public SavingsPlanReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    /// <summary>
    /// Returns an ordered list of aggregate time series points for the specified savings plan.
    /// </summary>
    /// <param name="planId">The savings plan identifier.</param>
    /// <param name="period">Aggregation period (Month, Quarter, HalfYear, Year).</param>
    /// <param name="take">Maximum number of points to return (ordered ascending by PeriodStart).</param>
    /// <param name="maxYearsBack">Optional limit for how many years back to consider (1..10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult with a read-only list of <see cref="TimeSeriesPointDto"/> or NotFound when the entity does not belong to the user.</returns>
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid planId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(planId, period, take, maxYearsBack, ct);
}

