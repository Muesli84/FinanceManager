using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.Reports;

/// <summary>
/// Controller providing aggregate time series for a single account (bank postings).
/// Delegates the actual work to <see cref="PostingReportsControllerBase"/>.
/// </summary>
[ApiController]
[Route("api/accounts/{accountId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AccountReportsController : PostingReportsControllerBase
{
    /// <summary>
    /// The posting kind this controller exposes (Bank postings).
    /// </summary>
    protected override PostingKind Kind => PostingKind.Bank;

    /// <summary>
    /// Creates a new instance of <see cref="AccountReportsController"/>.
    /// </summary>
    /// <param name="current">Current user service.</param>
    /// <param name="series">Posting time series service.</param>
    public AccountReportsController(ICurrentUserService current, IPostingTimeSeriesService series)
        : base(current, series) { }

    /// <summary>
    /// Returns an ordered list of aggregate time series points for the specified account.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="period">Aggregation period (Month, Quarter, HalfYear, Year).</param>
    /// <param name="take">Maximum number of points to return (ordered ascending by PeriodStart).</param>
    /// <param name="maxYearsBack">Optional limit for how many years back to consider (1..10).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult with a read-only list of <see cref="TimeSeriesPointDto"/> or NotFound when the entity does not belong to the user.</returns>
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid accountId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(accountId, period, take, maxYearsBack, ct);
}

