using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/{securityId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.Security;

    public SecurityReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid securityId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(securityId, period, take, maxYearsBack, ct);
}
