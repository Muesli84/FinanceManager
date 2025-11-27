using FinanceManager.Application;
using FinanceManager.Application.Reports;
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
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAsync(
        Guid securityId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(securityId, period, take, maxYearsBack, ct);
}
