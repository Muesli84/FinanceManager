using FinanceManager.Application;
using FinanceManager.Application.Attachments;
using FinanceManager.Application.Common;
using FinanceManager.Application.Savings;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Web.Infrastructure.ApiErrors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides CRUD, analysis, archiving and symbol management endpoints for savings plans owned by the current user.
/// </summary>
[ApiController]
[Route("api/savings-plans")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlansController : ControllerBase
{
    private const string Origin = "API_SavingsPlan";

    private readonly ISavingsPlanService _service;
    private readonly ICurrentUserService _current;
    private readonly IAttachmentService _attachments;
    private readonly ILogger<SavingsPlansController> _logger;
    private readonly IParentAssignmentService _parentAssign;
    private readonly IStringLocalizer<Controller> _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SavingsPlansController"/> class.
    /// </summary>
    /// <param name="service">The savings plan service used for business operations.</param>
    /// <param name="current">Service that provides information about the currently authenticated user.</param>
    /// <param name="attachments">The attachment service used for file uploads and management.</param>
    /// <param name="logger">Logger for the controller.</param>
    /// <param name="parentAssign">Service that manages parent assignments for entities.</param>
    /// <param name="localizer">Service for localized string retrieval.</param>
    public SavingsPlansController(
        ISavingsPlanService service,
        ICurrentUserService current,
        IAttachmentService attachments,
        ILogger<SavingsPlansController> logger,
        IParentAssignmentService parentAssign,
        IStringLocalizer<Controller> localizer)
    {
        _service = service;
        _current = current;
        _attachments = attachments;
        _logger = logger;
        _parentAssign = parentAssign;
        _localizer = localizer;
    }

    /// <summary>
    /// Lists savings plans (optionally only active ones).
    /// </summary>
    /// <param name="onlyActive">If true returns only non-archived plans.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing a 200 OK response with a list of <see cref="SavingsPlanDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SavingsPlanDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        var list = await _service.ListAsync(_current.UserId, onlyActive, ct);
        return Ok(list);
    }

    /// <summary>
    /// Returns count of savings plans (optionally only active ones).
    /// </summary>
    /// <param name="onlyActive">If true counts only non-archived plans.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing a 200 OK response with an object { count = number }.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("count")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    /// <summary>
    /// Gets a single savings plan by id.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains a <see cref="SavingsPlanDto"/> and a 200 OK status when found,
    /// or a 404 Not Found when the plan does not exist or does not belong to the current user.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpGet("{id:guid}", Name = "GetSavingsPlans")]
    [ProducesResponseType(typeof(SavingsPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Performs analytical aggregation for a savings plan (growth, progress etc.).
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> containing a 200 OK response with <see cref="SavingsPlanAnalysisDto"/>.</returns>
    /// <response code="200">The HTTP 200 response.</response>
    [HttpGet("{id:guid}/analysis")]
    [ProducesResponseType(typeof(SavingsPlanAnalysisDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnalyzeAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.AnalyzeAsync(id, _current.UserId, ct);
        return Ok(dto);
    }

    /// <summary>
    /// Creates a new savings plan.
    /// </summary>
    /// <param name="req">Creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains the created <see cref="SavingsPlanDto"/> and a 201 Created status.
    /// Returns 400 Bad Request if the request model is invalid.
    /// </returns>
    /// <response code="201">The HTTP 201 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    [HttpPost]
    [ProducesResponseType(typeof(SavingsPlanDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync([FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);

        await _parentAssign.TryAssignAsync(
            _current.UserId,
            req.Parent,
            createdKind: "savings-plans",
            createdId: dto.Id,
            ct);

        return CreatedAtRoute("GetSavingsPlans", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="req">Update request (same shape as create).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> that contains the updated <see cref="SavingsPlanDto"/> and a 200 OK status when the update succeeds,
    /// or a 404 Not Found when the plan does not exist or does not belong to the current user.
    /// Returns 400 Bad Request if the request model is invalid.
    /// </returns>
    /// <response code="200">The HTTP 200 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(SavingsPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Archives (soft-hides) a savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> with 204 No Content when archived or 404 Not Found when not found.</returns>
    /// <exception cref="ArgumentException">Thrown when the operation is invalid for the specified plan (mapped to 400 Bad Request).</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpPost("{id:guid}/archive")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentOutOfRangeException(Origin, ex, _localizer));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrorFactory.FromArgumentException(Origin, ex, _localizer));
        }
    }

    /// <summary>
    /// Permanently deletes a savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An <see cref="IActionResult"/> with 204 No Content when deleted, 404 Not Found when not found, or 400 Bad Request for invalid arguments.</returns>
    /// <exception cref="ArgumentException">Thrown when the operation is invalid for the specified plan (mapped to 400 Bad Request).</exception>
    /// <exception cref="Exception">Unexpected errors are logged by global error handling and result in a 500 response.</exception>
    /// <response code="204">The HTTP 204 response.</response>
    /// <response code="400">The HTTP 400 response.</response>
    /// <response code="404">The HTTP 404 response.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _service.DeleteAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
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
            _logger.LogError(ex, "Delete savings plan {SavingsPlanId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }

    /// <summary>
    /// Assigns a symbol attachment to the savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="attachmentId">Attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> with 204 No Content when the symbol was set successfully,
    /// or 404 Not Found when the plan or attachment cannot be found or is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown by the underlying service when arguments are invalid (mapped to 404).</exception>
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
    /// Clears any symbol attachment from the savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> with 204 No Content when the symbol was cleared successfully,
    /// or 404 Not Found when the plan cannot be found or the operation is invalid.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown by the underlying service when arguments are invalid (mapped to 404).</exception>
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
    /// Uploads a new symbol file and assigns it to the savings plan.
    /// </summary>
    /// <param name="id">Plan id.</param>
    /// <param name="file">Uploaded file.</param>
    /// <param name="categoryId">Optional attachment category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="IActionResult"/> with 200 OK and the created <see cref="AttachmentDto"/> when upload and assignment succeed,
    /// 400 Bad Request when the file is missing or invalid, or 500 Internal Server Error for unexpected failures.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when the underlying attachment service rejects the input (mapped to 400 Bad Request).</exception>
    /// <exception cref="Exception">Unexpected errors result in a 500 Internal Server Error.</exception>
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
            var key = $"{Origin}_Err_Invalid_File";
            var entry = _localizer[key];
            var message = entry.ResourceNotFound ? "File required" : entry.Value;
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Invalid_File", message));
        }

        try
        {
            using var stream = file.OpenReadStream();
            var dto = await _attachments.UploadAsync(_current.UserId, AttachmentEntityKind.SavingsPlan, id, stream, file.FileName, file.ContentType ?? "application/octet-stream", categoryId, AttachmentRole.Symbol, ct);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload symbol for savings plan {SavingsPlanId} failed", id);
            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorFactory.Unexpected(Origin, _localizer));
        }
    }
}