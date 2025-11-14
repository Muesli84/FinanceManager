using FinanceManager.Application;
using FinanceManager.Domain; // PostingKind
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides quarterly aggregated dividend amounts for securities owned by the current user.
/// Aggregation covers the period from January 1st of the previous year up to the current date.
/// </summary>
[ApiController]
[Route("api/securities/dividends")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class DividendsReportsController : ControllerBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly AppDbContext _db;

    /// <summary>
    /// Creates a new instance of <see cref="DividendsReportsController"/>.
    /// </summary>
    /// <param name="currentUser">Current user service to determine ownership.</param>
    /// <param name="db">Database context for querying postings and securities.</param>
    public DividendsReportsController(ICurrentUserService currentUser, AppDbContext db)
    {
        _currentUser = currentUser;
        _db = db;
    }

    /// <summary>
    /// DTO representing a single time series point (period start and aggregated amount).
    /// </summary>
    public sealed record TimeSeriesPointDto(DateTime PeriodStart, decimal Amount);

    private const int SecurityPostingSubType_Dividend = 2; // matches client enum mapping

    /// <summary>
    /// Returns quarterly aggregated dividend amounts (security postings with dividend subtype) starting from January 1st of the previous year.
    /// The optional query parameters are accepted for compatibility with the client but are ignored (always quarterly, full range).
    /// </summary>
    /// <param name="period">Aggregation period (ignored).</param>
    /// <param name="take">Maximum number of points to return (ignored).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>ActionResult containing a read-only list of <see cref="TimeSeriesPointDto"/>.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TimeSeriesPointDto>>> GetAsync([FromQuery] string? period = null, [FromQuery] int? take = null, CancellationToken ct = default)
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
            return Ok(Array.Empty<TimeSeriesPointDto>());
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
            .Select(g => new TimeSeriesPointDto(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        return Ok(groups);
    }

    /// <summary>
    /// Returns the first day of the quarter for the provided date.
    /// </summary>
    private static DateTime QuarterStart(DateTime d)
    {
        int qMonth = ((d.Month - 1) / 3) * 3 + 1; // 1,4,7,10
        return new DateTime(d.Year, qMonth, 1);
    }
}
