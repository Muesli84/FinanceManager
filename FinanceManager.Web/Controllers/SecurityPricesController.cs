using FinanceManager.Application;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/{id:guid}/prices")]
[Authorize]
public sealed class SecurityPricesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private const int MaxTake = 250;

    public SecurityPricesController(AppDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    public sealed record SecurityPriceDto(DateTime Date, decimal Close);

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SecurityPriceDto>>> ListAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, MaxTake);
        var owned = await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == id && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }

        var q = _db.SecurityPrices.AsNoTracking()
            .Where(p => p.SecurityId == id)
            .OrderByDescending(p => p.Date)
            .Skip(skip)
            .Take(take);

        var list = await q.Select(p => new SecurityPriceDto(p.Date, p.Close)).ToListAsync(ct);
        return Ok(list);
    }
}