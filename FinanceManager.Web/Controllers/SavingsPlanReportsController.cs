using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/savings-plans/{planId:guid}/aggregates")]
[Authorize]
public sealed class SavingsPlanReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.SavingsPlan;

    public SavingsPlanReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid planId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        CancellationToken ct = default)
        => GetInternalAsync(planId, period, take, ct);
}
