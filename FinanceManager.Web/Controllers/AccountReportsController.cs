using FinanceManager.Application;
using FinanceManager.Application.Reports;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/accounts/{accountId:guid}/aggregates")]
[Authorize]
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
        CancellationToken ct = default)
        => GetInternalAsync(accountId, period, take, ct);
}

[ApiController]
[Route("api/accounts/aggregates")]
[Authorize]
public sealed class AccountsAllReportsController : PostingReportsControllerBase
{
    protected override PostingKind Kind => PostingKind.Bank;

    public AccountsAllReportsController(ICurrentUserService current, IPostingTimeSeriesService series)
        : base(current, series) { }

    [HttpGet]
    public Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAllAsync(
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        CancellationToken ct = default)
        => GetAllInternalAsync(period, take, ct);
}
