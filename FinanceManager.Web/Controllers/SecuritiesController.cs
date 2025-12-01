using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Application.Reports;
using FinanceManager.Application.Securities;
using FinanceManager.Domain.Attachments;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/securities")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class SecuritiesController : ControllerBase
{
    private readonly ISecurityService _service;
    private readonly ICurrentUserService _current;
    private readonly IAttachmentService _attachments;
    private readonly AppDbContext _db;
    private readonly IBackgroundTaskManager _tasks;
    private readonly IPostingTimeSeriesService _series;

    public SecuritiesController(
        ISecurityService service,
        ICurrentUserService current,
        IAttachmentService attachments,
        IPostingTimeSeriesService series,
        AppDbContext db,
        IBackgroundTaskManager tasks)
    {
        _service = service; _current = current; _attachments = attachments; _db = db; _tasks = tasks; _series = series;
    }

    // CRUD -----------------------------------------------------------------
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await _service.ListAsync(_current.UserId, onlyActive, ct));

    [HttpGet("count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    [HttpGet("{id:guid}", Name = "GetSecurityAsync")]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        return CreatedAtRoute("GetSecurityAsync", new { id = dto.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct);
            return NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/symbol")]
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadSymbolAsync(Guid id, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, CancellationToken ct)
    {
        if (file == null) { return BadRequest(new { error = "File required" }); }
        try
        {
            using var stream = file.OpenReadStream();
            var dto = await _attachments.UploadAsync(_current.UserId, AttachmentEntityKind.Security, id, stream, file.FileName, file.ContentType ?? "application/octet-stream", categoryId, AttachmentRole.Symbol, ct);
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, dto.Id, ct);
            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    // Aggregates / Prices / Backfill / Dividends ---------------------------
    private static int? NormalizeYears(int? maxYearsBack)
    {
        if (!maxYearsBack.HasValue) return null;
        return Math.Clamp(maxYearsBack.Value, 1, 10);
    }

    private static AggregatePeriod ParsePeriod(string period)
    {
        if (!Enum.TryParse<AggregatePeriod>(period, true, out var p))
        {
            p = AggregatePeriod.Month;
        }
        return p;
    }

    private static int NormalizeTake(AggregatePeriod p, int take)
    {
        var def = p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10;
        return Math.Clamp(take <= 0 ? def : take, 1, 200);
    }

    private async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAggregatesInternalAsync(Guid securityId, string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        var p = ParsePeriod(period);
        take = NormalizeTake(p, take);
        var years = NormalizeYears(maxYearsBack);
        var data = await _series.GetAsync(_current.UserId, PostingKind.Security, securityId, p, take, years, ct);
        if (data == null) return NotFound();
        return Ok(data.Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList());
    }

    // GET api/securities/{securityId}/aggregates
    [HttpGet("{securityId:guid}/aggregates")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAggregatesAsync(
        Guid securityId,
        [FromQuery] string period = "Month",
        [FromQuery] int take = 36,
        [FromQuery] int? maxYearsBack = null,
        CancellationToken ct = default)
        => GetAggregatesInternalAsync(securityId, period, take, maxYearsBack, ct);

    // GET api/securities/{id}/prices
    [HttpGet("{id:guid}/prices")]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityPriceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SecurityPriceDto>>> GetPricesAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        const int MaxTake = 250;
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

    // POST api/securities/backfill
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<BackgroundTaskInfo> EnqueueBackfill([FromBody] SecurityBackfillRequest req)
    {
        var payload = new { SecurityId = req.SecurityId?.ToString(), FromDateUtc = req.FromDateUtc?.ToString("o"), ToDateUtc = req.ToDateUtc?.ToString("o") };
        var info = _tasks.Enqueue(BackgroundTaskType.SecurityPricesBackfill, _current.UserId, payload, allowDuplicate: false);
        return Ok(info);
    }

    // GET api/securities/dividends
    [HttpGet("dividends")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetDividendsAsync([FromQuery] string? period = null, [FromQuery] int? take = null, CancellationToken ct = default)
    {
        const int SecurityPostingSubType_Dividend = 2; // matches client enum mapping
        var userId = _current.UserId;
        var today = DateTime.UtcNow.Date;
        var start = new DateTime(today.Year - 1, 1, 1);

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

        static DateTime QuarterStart(DateTime d)
        {
            int qMonth = ((d.Month - 1) / 3) * 3 + 1; // 1,4,7,10
            return new DateTime(d.Year, qMonth, 1);
        }

        var groups = raw
            .GroupBy(x => QuarterStart(x.BookingDate))
            .Select(g => new AggregatePointDto(g.Key, g.Sum(x => x.Amount)))
            .OrderBy(x => x.PeriodStart)
            .ToList();

        return Ok(groups);
    }
}
