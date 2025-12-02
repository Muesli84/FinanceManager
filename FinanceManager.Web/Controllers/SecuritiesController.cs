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

/// <summary>
/// Manages securities: CRUD, symbol attachment, price queries, time series aggregates,
/// dividends aggregation and background price backfill tasks.
/// </summary>
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

    public SecuritiesController(ISecurityService service, ICurrentUserService current, IAttachmentService attachments, IPostingTimeSeriesService series, AppDbContext db, IBackgroundTaskManager tasks)
    { _service = service; _current = current; _attachments = attachments; _db = db; _tasks = tasks; _series = series; }

    /// <summary>
    /// Lists securities for the current user (optionally only active).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await _service.ListAsync(_current.UserId, onlyActive, ct));

    /// <summary>
    /// Returns count of securities (optionally only active).
    /// </summary>
    [HttpGet("count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    /// <summary>
    /// Gets a single security by id.
    /// </summary>
    [HttpGet("{id:guid}", Name = "GetSecurityAsync")]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Creates a new security.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct);
        return CreatedAtRoute("GetSecurityAsync", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing security.
    /// </summary>
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

    /// <summary>
    /// Archives (soft-hides) a security.
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes a security permanently.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Assigns a symbol attachment to a security.
    /// </summary>
    [HttpPost("{id:guid}/symbol/{attachmentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetSymbolAsync(Guid id, Guid attachmentId, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, attachmentId, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }

    /// <summary>
    /// Clears a symbol attachment from the security.
    /// </summary>
    [HttpDelete("{id:guid}/symbol")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearSymbolAsync(Guid id, CancellationToken ct)
    {
        try { await _service.SetSymbolAttachmentAsync(id, _current.UserId, null, ct); return NoContent(); }
        catch (ArgumentException) { return NotFound(); }
    }

    /// <summary>
    /// Uploads and assigns a new symbol file to the security.
    /// </summary>
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
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception) { return Problem("Unexpected error", statusCode: 500); }
    }

    private static int? NormalizeYears(int? maxYearsBack) { if (!maxYearsBack.HasValue) return null; return Math.Clamp(maxYearsBack.Value, 1, 10); }
    private static AggregatePeriod ParsePeriod(string period) { if (!Enum.TryParse<AggregatePeriod>(period, true, out var p)) { p = AggregatePeriod.Month; } return p; }
    private static int NormalizeTake(AggregatePeriod p, int take) { var def = p == AggregatePeriod.Month ? 36 : p == AggregatePeriod.Quarter ? 16 : p == AggregatePeriod.HalfYear ? 12 : 10; return Math.Clamp(take <= 0 ? def : take, 1, 200); }

    private async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAggregatesInternalAsync(Guid securityId, string period, int take, int? maxYearsBack, CancellationToken ct)
    {
        var p = ParsePeriod(period); take = NormalizeTake(p, take); var years = NormalizeYears(maxYearsBack);
        var data = await _series.GetAsync(_current.UserId, PostingKind.Security, securityId, p, take, years, ct);
        if (data == null) return NotFound();
        return Ok(data.Select(a => new AggregatePointDto(a.PeriodStart, a.Amount)).ToList());
    }

    /// <summary>
    /// Returns time series aggregates for a security.
    /// </summary>
    [HttpGet("{securityId:guid}/aggregates")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAggregatesAsync(Guid securityId, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => GetAggregatesInternalAsync(securityId, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns historical prices (paged latest first) for a security.
    /// </summary>
    [HttpGet("{id:guid}/prices")]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityPriceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SecurityPriceDto>>> GetPricesAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        const int MaxTake = 250;
        take = Math.Clamp(take, 1, MaxTake);
        var owned = await _db.Securities.AsNoTracking().AnyAsync(s => s.Id == id && s.OwnerUserId == _current.UserId, ct);
        if (!owned) { return NotFound(); }
        var q = _db.SecurityPrices.AsNoTracking().Where(p => p.SecurityId == id).OrderByDescending(p => p.Date).Skip(skip).Take(take);
        var list = await q.Select(p => new SecurityPriceDto(p.Date, p.Close)).ToListAsync(ct);
        return Ok(list);
    }

    /// <summary>
    /// Enqueues a background task for backfilling security prices.
    /// </summary>
    [HttpPost("backfill")]
    [ProducesResponseType(typeof(BackgroundTaskInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<BackgroundTaskInfo> EnqueueBackfill([FromBody] SecurityBackfillRequest req)
    {
        var payload = new { SecurityId = req.SecurityId?.ToString(), FromDateUtc = req.FromDateUtc?.ToString("o"), ToDateUtc = req.ToDateUtc?.ToString("o") };
        var info = _tasks.Enqueue(BackgroundTaskType.SecurityPricesBackfill, _current.UserId, payload, allowDuplicate: false);
        return Ok(info);
    }

    /// <summary>
    /// Returns quarterly dividend aggregates for the past year across all owned securities.
    /// </summary>
    [HttpGet("dividends")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetDividendsAsync([FromQuery] string? period = null, [FromQuery] int? take = null, CancellationToken ct = default)
    {
        const int SecurityPostingSubType_Dividend = 2;
        var userId = _current.UserId;
        var today = DateTime.UtcNow.Date;
        var start = new DateTime(today.Year - 1, 1, 1);
        var securityIds = await _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == userId).Select(s => s.Id).ToListAsync(ct);
        if (securityIds.Count == 0) { return Ok(Array.Empty<AggregatePointDto>()); }
        var raw = await _db.Postings.AsNoTracking().Where(p => p.Kind == PostingKind.Security).Where(p => p.SecuritySubType.HasValue && (int)p.SecuritySubType.Value == SecurityPostingSubType_Dividend).Where(p => p.SecurityId != null && securityIds.Contains(p.SecurityId.Value)).Where(p => p.BookingDate >= start).Select(p => new { p.BookingDate, p.Amount }).ToListAsync(ct);
        static DateTime QuarterStart(DateTime d) { int qMonth = ((d.Month - 1) / 3) * 3 + 1; return new DateTime(d.Year, qMonth, 1); }
        var groups = raw.GroupBy(x => QuarterStart(x.BookingDate)).Select(g => new AggregatePointDto(g.Key, g.Sum(x => x.Amount))).OrderBy(x => x.PeriodStart).ToList();
        return Ok(groups);
    }
}
