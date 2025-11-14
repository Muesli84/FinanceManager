using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Aggregated time series endpoints for a specific security.
/// Inherits aggregation behavior from PostingReportsControllerBase.
/// </summary>
[ApiController]
[Route("api/securities/{securityId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityReportsController : PostingReportsControllerBase
{
    /// <summary>
    /// The posting kind that this controller works with: Security postings.
    /// </summary>
    protected override PostingKind Kind => PostingKind.Security;

    /// <summary>
    /// Creates a new instance of <see cref="SecurityReportsController"/>.
    /// </summary>
    public SecurityReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    /// <summary>
    /// Returns aggregated time series points for the given security.
    /// </summary>
    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid securityId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(securityId, period, take, maxYearsBack, ct);
}

