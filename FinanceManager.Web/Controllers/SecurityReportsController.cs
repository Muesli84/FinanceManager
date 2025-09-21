using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/{securityId:guid}/aggregates")]
[Authorize]
public sealed class SecurityReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.Security;

    public SecurityReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid securityId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        CancellationToken ct = default)
        => GetInternalAsync(securityId, period, take, ct);
}
