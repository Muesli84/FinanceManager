using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/accounts/{accountId:guid}/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AccountReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.Bank;

    public AccountReportsController(ICurrentUserService current, IPostingTimeSeriesService series)
        : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync(
        Guid accountId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetInternalAsync(accountId, period, take, maxYearsBack, ct);
}

[ApiController]
[Route("api/accounts/aggregates")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class AccountsAllReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.Bank;

    public AccountsAllReportsController(ICurrentUserService current, IPostingTimeSeriesService series)
        : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAllAsync(
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetAllInternalAsync(period, take, maxYearsBack, ct);
}
