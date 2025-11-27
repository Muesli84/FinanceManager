using FinanceManager.Application;
using FinanceManager.Application.Reports;
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
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAllAsync(
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetAllInternalAsync(period, take, maxYearsBack, ct);
}
