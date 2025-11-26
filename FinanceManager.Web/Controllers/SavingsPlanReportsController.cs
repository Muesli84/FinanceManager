using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/savings-plans/{planId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlanReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.SavingsPlan;

    public SavingsPlanReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAsync(
        Guid planId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(planId, period, take, maxYearsBack, ct);
}

[ApiController]
[Route("api/savings-plans/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlansAllReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.SavingsPlan;

    public SavingsPlansAllReportsController(ICurrentUserService current, IPostingTimeSeriesService series) : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAllAsync(
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetAllInternalAsync(period, take, maxYearsBack, ct);
}
