using FinanceManager.Application;
using FinanceManager.Domain;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/accounts/{accountId:guid}/aggregates")]
[Authorize]
public sealed class AccountReportsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;

    public AccountReportsController(AppDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public sealed record AggregatePointDto(DateTime PeriodStart, decimal Amount);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAsync(Guid accountId, [FromQuery] string period = "Month", [FromQuery] int take = 36, CancellationToken ct = default)
    {
        var owned = await _db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        if (!Enum.TryParse<AggregatePeriod>(period, ignoreCase: true, out var p))
        {
            p = AggregatePeriod.Month;
        }
        // sensible defaults
        take = Math.Clamp(take <= 0 ? (p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : 10) : take, 1, 200);

        var q = _db.PostingAggregates.AsNoTracking()
            .Where(x => x.Kind == PostingKind.Bank && x.AccountId == accountId && x.Period == p);

        // Take latest N, then order ascending for chart
        var latest = await q.OrderByDescending(x => x.PeriodStart).Take(take).ToListAsync(ct);
        var result = latest.OrderBy(x => x.PeriodStart).Select(x => new AggregatePointDto(x.PeriodStart, x.Amount)).ToList();
        return Ok(result);
    }
}
