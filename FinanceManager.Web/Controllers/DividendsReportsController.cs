using FinanceManager.Application;
using FinanceManager.Domain; // PostingKind
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities/dividends")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class DividendsReportsController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly AppDbContext _db;
    public DividendsReportsController(ICurrentUserService currentUser, AppDbContext db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    private const int SecurityPostingSubType_Dividend = 2; // matches client enum mapping

    /// <summary>
    /// Returns quarterly aggregated dividend amounts (Security postings with sub type Dividend) starting from January 1st of the previous year up to the current date.
    /// The optional 'period' and 'take' parameters are accepted for compatibility with the generic chart component but are ignored (always quarterly, full range).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAsync([FromQuery] string? period = null, [FromQuery] int? take = null, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId;
        var today = DateTime.UtcNow.Date;
        var start = new DateTime(today.Year - 1, 1, 1);

        // Preload owned security ids
        var securityIds = await _db.Securities.AsNoTracking()
            .Where(s => s.OwnerUserId == userId)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (securityIds.Count == 0)
        {
            return Ok(Array.Empty<AggregatePointDto>());
        }

        var raw = await _db.Postings.AsNoTracking()
            .Where(p => p.Kind == PostingKind.Security)
            .Where(p => p.SecuritySubType.HasValue && (int)p.SecuritySubType.Value == SecurityPostingSubType_Dividend)
            .Where(p => p.SecurityId != null && securityIds.Contains(p.SecurityId.Value))
            .Where(p => p.BookingDate >= start)
            .Select(p => new { p.BookingDate, p.Amount })
            .ToListAsync(ct);

        var groups = raw
            .GroupBy(x => QuarterStart(x.BookingDate))
            .Select(g => new AggregatePointDto(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        return Ok(groups);
    }

    private static DateTime QuarterStart(DateTime d)
    {
        int qMonth = ((d.Month - 1) / 3) * 3 + 1; // 1,4,7,10
        return new DateTime(d.Year, qMonth, 1);
    }
}
