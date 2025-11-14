using FinanceManager.Application;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides historical security prices for a given security.
/// </summary>
[ApiController]
[Route("api/securities/{id:guid}/prices")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SecurityPricesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private const int MaxTake = 250;

    /// <summary>
    /// Creates a new instance of <see cref="SecurityPricesController"/>.
    /// </summary>
    public SecurityPricesController(AppDbContext db, ICurrentUserService current)
    { _db = db; _current = current; }

    /// <summary>
    /// DTO for security price points.
    /// </summary>
    public sealed record SecurityPriceDto(DateTime Date, decimal Close);

    /// <summary>
    /// Lists historical prices for the specified security with pagination.
    /// </summary>
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