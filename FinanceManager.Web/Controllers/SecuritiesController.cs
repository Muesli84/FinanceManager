using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Application.Reports;
using FinanceManager.Application.Securities;
using FinanceManager.Application.Securities.ReturnAnalysis;
using FinanceManager.Domain.Attachments;
using FinanceManager.Domain.Postings;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using FinanceManager.Application.Common;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.Extensions.Localization;
using FinanceManager.Web.Controllers;

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
    private const string Origin = "API_Securities";

    private readonly ISecurityService _service;
    private readonly ICurrentUserService _current;
    private readonly IAttachmentService _attachments;
    private readonly IBackgroundTaskManager _tasks;
    private readonly IPostingTimeSeriesService _series;
    private readonly ISecurityPriceService _priceService;
    private readonly ISecurityPriceImportServiceFactory _priceImportFactory;
    private readonly ISecurityReportService _reports;
    private readonly ILogger<SecuritiesController> _logger;
    private readonly IParentAssignmentService _parentAssign;
    private readonly IStringLocalizer<Controller> _localizer;
    private readonly IReturnAnalysisService _returnAnalysis;

    /// <summary>
    /// Initializes a new instance of <see cref="SecuritiesController"/>.
    /// </summary>
    /// <param name="service">Service providing business operations for securities.</param>
    /// <param name="current">Service that provides information about the current user.</param>
    /// <param name="attachments">Service managing attachments (upload, storage).</param>
    /// <param name="series">Service to compute posting time series and aggregates.</param>
    /// <param name="priceService">Service to retrieve security price history.</param>
    /// <param name="priceImportFactory">Factory resolving provider-specific security price import services.</param>
    /// <param name="reports">Service providing security reports and aggregations.</param>
    /// <param name="tasks">Background task manager used to enqueue asynchronous background jobs.</param>
    /// <param name="logger">Logger used for diagnostic messages.</param>
    /// <param name="parentAssign">Service that manages server-side create-and-assign operations.</param>
    /// <param name="localizer">Localizer for generating user-friendly error messages.</param>
    /// <param name="returnAnalysis">Service providing return analysis calculations.</param>
    public SecuritiesController(
        ISecurityService service,
        ICurrentUserService current,
        IAttachmentService attachments,
        IBackgroundTaskManager tasks,
        IPostingTimeSeriesService series,
        ISecurityPriceService priceService,
        ISecurityPriceImportServiceFactory priceImportFactory,
        ISecurityReportService reports,
        ILogger<SecuritiesController> logger,
        IParentAssignmentService parentAssign,
        IStringLocalizer<Controller> localizer,
        IReturnAnalysisService returnAnalysis)
    {
        _service = service;
        _current = current;
        _attachments = attachments;
        _tasks = tasks;
        _series = series;
        _priceService = priceService;
        _priceImportFactory = priceImportFactory;
        _reports = reports;
        _logger = logger;
        _parentAssign = parentAssign;
        _localizer = localizer;
        _returnAnalysis = returnAnalysis;
    }

    /// <summary>
    /// Lists securities for the current user.
    /// </summary>
    /// <param name="onlyActive">When true only active securities are returned; otherwise archived ones are included.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>200 OK with a list of <see cref="SecurityDto"/> objects.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await _service.ListAsync(_current.UserId, onlyActive, ct));

    /// <summary>
    /// Returns count of securities for the current user.
    /// </summary>
    /// <param name="onlyActive">When true only count active securities; otherwise count all.</param>
    /// <param name="ct">Cancellation token to cancel the operation.</param>
    /// <returns>200 OK with an object containing the count.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    /// <summary>
    /// Gets a single security by id for the current user.
    /// </summary>
    /// <param name="id">Security identifier (GUID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="SecurityDto"/> when found; 404 NotFound when the security doesn't exist or is not owned by the current user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}", Name = "GetSecurityAsync")]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct = default)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Creates a new security owned by the current user.
    /// </summary>
    /// <param name="req">Security create request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with the created <see cref="SecurityDto"/>, or 400 Bad Request when validation fails.</returns>
    /// <exception cref="ArgumentException">May be thrown by the service for invalid input; will be mapped to 400 Bad Request.</exception>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        try
        {
            var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct, req.Region, req.Sector);

            await _parentAssign.TryAssignAsync(
                _current.UserId,
                req.Parent,
                createdKind: "securities",
                createdId: dto.Id,
                ct);

            return CreatedAtRoute("GetSecurityAsync", new { id = dto.Id }, dto);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Create security failed");
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Updates an existing security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="req">Update request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with updated <see cref="SecurityDto"/>, 404 NotFound when not found, or 400 BadRequest for invalid input.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SecurityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SecurityRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        try
        {
            var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Identifier, req.Description, req.AlphaVantageCode, req.CurrencyCode, req.CategoryId, ct, req.Region, req.Sector);
            return dto == null ? NotFound() : Ok(dto);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update security {SecurityId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Archives (soft-hides) a security so it is no longer shown in active lists.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 NoContent on success; 404 NotFound when the security does not exist or is not owned by the user.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 NoContent on success; 404 NotFound when the security does not exist or is not owned by the user.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
    /// <param name="id">Security identifier.</param>
    /// <param name="attachmentId">Attachment identifier to set as symbol.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 NoContent on success; 404 NotFound when the security or attachment is invalid.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
    }

    /// <summary>
    /// Clears a symbol attachment from the security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 NoContent on success; 404 NotFound when the security is invalid.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
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
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return NotFound(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
    }

    /// <summary>
    /// Uploads and assigns a new symbol file to the security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="file">Form file containing the uploaded symbol image or asset.</param>
    /// <param name="categoryId">Optional category id to classify the attachment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with the created <see cref="AttachmentDto"/>, or 400 Bad Request for invalid input, or 500 on unexpected errors.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="500">The HTTP 500 response.</response>
    [HttpPost("{id:guid}/symbol")]
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UploadSymbolAsync(Guid id, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, CancellationToken ct)
    {
        if (file == null)
        {
            var err = ApiErrorDto.Create(Origin, "Err_Invalid_File", _localizer[$"{Origin}_Err_Invalid_File"].ResourceNotFound
                ? "File required"
                : _localizer[$"{Origin}_Err_Invalid_File"].Value);

            return BadRequest(err);
        }

        try
        {
            using var stream = file.OpenReadStream();
            var dto = await _attachments.UploadAsync(_current.UserId, AttachmentEntityKind.Security, id, stream, file.FileName, file.ContentType ?? "application/octet-stream", categoryId, AttachmentRole.Symbol, ct);
            await _service.SetSymbolAttachmentAsync(id, _current.UserId, dto.Id, ct);
            return Ok(dto);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
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
    /// <param name="securityId">Security identifier.</param>
    /// <param name="period">Aggregate period name (e.g. "Month", "Quarter"). Case-insensitive.</param>
    /// <param name="take">Number of periods to take. Will be normalized to a safe range.</param>
    /// <param name="maxYearsBack">Optional maximum years back to include.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="AggregatePointDto"/>, or 404 NotFound when no data is available.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{securityId:guid}/aggregates")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetAggregatesAsync(Guid securityId, [FromQuery] string period = "Month", [FromQuery] int take = 36, [FromQuery] int? maxYearsBack = null, CancellationToken ct = default)
        => GetAggregatesInternalAsync(securityId, period, take, maxYearsBack, ct);

    /// <summary>
    /// Returns historical prices (paged latest first) for a security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="skip">Number of records to skip (paging offset).</param>
    /// <param name="take">Number of records to take (page size). Clamped to a maximum for safety.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="SecurityPriceDto"/>, or 404 NotFound when the security is not owned by the current user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/prices")]
    [ProducesResponseType(typeof(IReadOnlyList<SecurityPriceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SecurityPriceDto>>> GetPricesAsync(Guid id, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        const int MaxTake = 250;
        take = Math.Clamp(take, 1, MaxTake);
        var list = await _priceService.ListAsync(_current.UserId, id, skip, take, ct);
        return Ok(list);
    }

    /// <summary>
    /// Imports security prices from an uploaded provider CSV file.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="file">Uploaded CSV file.</param>
    /// <param name="provider">Optional provider hint (defaults to "ing").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// 200 OK with <see cref="SecurityPriceImportResultDto"/> when at least one valid row was processed;
    /// 400 BadRequest when no valid rows exist or provider is unsupported.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    /// <response code="500">The HTTP 500 response.</response>
    [HttpPost("{id:guid}/prices/import")]
    [RequestSizeLimit(long.MaxValue)]
    [ProducesResponseType(typeof(SecurityPriceImportResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ImportPricesAsync(
        Guid id,
        [FromForm] IFormFile? file,
        [FromForm] string? provider,
        CancellationToken ct)
    {
        _logger.LogInformation("Importing prices for security {SecurityId} with provider {Provider}", id, provider ?? "ing");

        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid_File", "A non-empty file is required."));
        }

        try
        {
            var security = await _service.GetAsync(id, _current.UserId, ct);
            if (security == null)
            {
                return NotFound();
            }

            var context = new SecurityPriceImportContext(provider ?? "ing", file.FileName, file.ContentType);
            var importService = _priceImportFactory.Resolve(context);

            await using var stream = file.OpenReadStream();
            var result = await importService.ImportAsync(_current.UserId, id, stream, context, ct);
            if (result.Inserted + result.Updated + result.Unchanged == 0)
            {
                return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid_Import", "No valid price rows were found in the uploaded file."));
            }

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, new ArgumentException(ex.Message, nameof(provider)), _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import prices failed for security {SecurityId}", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Enqueues a background task for backfilling security prices.
    /// </summary>
    /// <param name="req">Backfill request containing security id and optional date range.</param>
    /// <returns>200 OK with <see cref="BackgroundTaskInfo"/> describing the enqueued task.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
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
    /// <param name="period">Optional period parameter (ignored; kept for compatibility).</param>
    /// <param name="take">Optional take parameter (ignored; kept for compatibility).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of quarterly <see cref="AggregatePointDto"/> objects.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("dividends")]
    [ProducesResponseType(typeof(IReadOnlyList<AggregatePointDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AggregatePointDto>>> GetDividendsAsync([FromQuery] string? period = null, [FromQuery] int? take = null, CancellationToken ct = default)
    {
        var data = await _reports.GetDividendAggregatesAsync(_current.UserId, ct);
        return Ok(data);
    }

    /// <summary>
    /// Returns the compact return summary for a security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="ReturnSummaryDto"/>; 404 when not found or not owned by user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-summary")]
    [ProducesResponseType(typeof(ReturnSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReturnSummaryAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting return summary for security {SecurityId}", id);
        var result = await _returnAnalysis.GetReturnSummaryAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns sparkline chart data for a security (separate from summary to keep cache lean).
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="SparklineDataDto"/>; 404 when not found or insufficient price data.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-sparkline")]
    [ProducesResponseType(typeof(SparklineDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSparklineDataAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting sparkline data for security {SecurityId}", id);
        var result = await _returnAnalysis.GetSparklineDataAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns detailed return metrics for the Kennzahlen tab.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="DetailedReturnMetricsDto"/>; 404 when not found or not owned by user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-metrics")]
    [ProducesResponseType(typeof(DetailedReturnMetricsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReturnMetricsAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting detailed return metrics for security {SecurityId}", id);
        var result = await _returnAnalysis.GetDetailedMetricsAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns periodic returns (annual + monthly + dividends) for the Zeitliche Entwicklung tab.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="PeriodicReturnsDto"/>; 404 when not found or not owned by user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-periodic")]
    [ProducesResponseType(typeof(PeriodicReturnsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPeriodicReturnsAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting periodic returns for security {SecurityId}", id);
        var result = await _returnAnalysis.GetPeriodicReturnsAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns cashflow timeline for the Cashflows tab.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="CashflowTimelineDto"/>; 404 when not found or not owned by user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-cashflows")]
    [ProducesResponseType(typeof(CashflowTimelineDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCashflowTimelineAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting cashflow timeline for security {SecurityId}", id);
        var result = await _returnAnalysis.GetCashflowTimelineAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns performance chart data.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="timeRange">Time range for the chart (default: All).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="PerformanceChartDataDto"/>; 404 when not found or not owned by user.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-chart")]
    [ProducesResponseType(typeof(PerformanceChartDataDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPerformanceChartAsync(Guid id, [FromQuery] ChartTimeRange timeRange = ChartTimeRange.All, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting performance chart data for security {SecurityId}, range {TimeRange}", id, timeRange);
        var result = await _returnAnalysis.GetPerformanceChartDataAsync(id, _current.UserId, timeRange, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns benchmark comparison data.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="BenchmarkComparisonDto"/>; 404 when no benchmark is configured or data is insufficient.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-benchmark")]
    [ProducesResponseType(typeof(BenchmarkComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBenchmarkComparisonAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting benchmark comparison for security {SecurityId}", id);
        var result = await _returnAnalysis.GetBenchmarkComparisonAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Returns return analysis settings for the current user.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="ReturnAnalysisSettingsDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("return-analysis/settings")]
    [ProducesResponseType(typeof(ReturnAnalysisSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReturnAnalysisSettingsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Getting return analysis settings for user {UserId}", _current.UserId);
        var result = await _returnAnalysis.GetUserSettingsAsync(_current.UserId, ct);
        return Ok(result ?? new ReturnAnalysisSettingsDto(null, null, false, 0));
    }

    /// <summary>
    /// Updates return analysis settings for the current user.
    /// </summary>
    /// <param name="req">Settings request payload.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 NoContent on success; 400 BadRequest for invalid input.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPut("return-analysis/settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateReturnAnalysisSettingsAsync([FromBody] ReturnAnalysisSettingsRequest req, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) { return ValidationProblem(ModelState); }
        _logger.LogInformation("Updating return analysis settings for user {UserId}", _current.UserId);
        try
        {
            await _returnAnalysis.UpdateUserSettingsAsync(_current.UserId, req.BenchmarkSecurityId, req.ShowSharpeRatio, req.RiskFreeRate, ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update return analysis settings failed for user {UserId}", _current.UserId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected error" });
        }
    }

    /// <summary>
    /// Invalidates the return analysis cache for a specific security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <returns>204 NoContent on success.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpDelete("{id:guid}/return-cache")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> InvalidateReturnCacheAsync(Guid id)
    {
        _logger.LogInformation("Invalidating return analysis cache for security {SecurityId}", id);
        await _returnAnalysis.InvalidateCacheAsync(id, _current.UserId);
        return NoContent();
    }

    /// <summary>
    /// Invalidates the return analysis cache for all securities of the current user.
    /// </summary>
    /// <returns>204 NoContent on success.</returns>
    /// <response code="204">The HTTP 204 response.</response>
    [HttpDelete("return-cache")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> InvalidateAllReturnCacheAsync()
    {
        _logger.LogInformation("Invalidating all return analysis cache for user {UserId}", _current.UserId);
        await _returnAnalysis.InvalidateAllUserCachesAsync(_current.UserId);
        return NoContent();
    }

    /// <summary>
    /// Returns the KPI formula and cashflow breakdown for all widget KPIs of a security.
    /// </summary>
    /// <param name="id">Security identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with list of <see cref="KpiBreakdownDto"/>; 404 when not found or no data.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}/return-kpi-breakdowns")]
    [ProducesResponseType(typeof(IReadOnlyList<KpiBreakdownDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetKpiBreakdownsAsync(Guid id, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting KPI breakdowns for security {SecurityId}", id);
        var result = await _returnAnalysis.GetKpiBreakdownsAsync(id, _current.UserId, ct);
        return result == null ? NotFound() : Ok(result);
    }
}
