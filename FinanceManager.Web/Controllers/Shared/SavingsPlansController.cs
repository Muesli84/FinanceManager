using FinanceManager.Application.Savings;
using FinanceManager.Domain.Savings;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Manages savings plans for the current user (list, create, update, archive, delete and symbol attachments).
/// Controller is intentionally thin and delegates business logic to <see cref="ISavingsPlanService"/>.
/// </summary>
[ApiController]
[Route("api/savings-plans")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class SavingsPlansController : ControllerBase
{
    private readonly ISavingsPlanService _service;
    private readonly FinanceManager.Application.ICurrentUserService _current;
    private readonly IAttachmentService _attachments;

    /// <summary>
    /// Creates a new instance of <see cref="SavingsPlansController"/>.
    /// </summary>
    /// <param name="service">Savings plan service.</param>
    /// <param name="current">Current user service.</param>
    /// <param name="attachments">Attachment service.</param>
    public SavingsPlansController(ISavingsPlanService service, FinanceManager.Application.ICurrentUserService current, IAttachmentService attachments)
    {
        _service = service;
        _current = current;
        _attachments = attachments;
    }

    /// <summary>
    /// Request payload for creating or updating a savings plan.
    /// </summary>
    public sealed record SavingsPlanCreateRequest(
        [Required, MinLength(2)] string Name,
        SavingsPlanType Type,
        decimal? TargetAmount,
        DateTime? TargetDate,
        SavingsPlanInterval? Interval,
        Guid? CategoryId,
        string? ContractNumber
    );

    /// <summary>
    /// Lists savings plans for the current user.
    /// </summary>
    /// <param name="onlyActive">If true, only active plans are returned.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="SavingsPlanDto"/>.</returns>
    [HttpGet]
    public async Task<IActionResult> ListAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
    {
        var list = await _service.ListAsync(_current.UserId, onlyActive, ct);
        return Ok(list);
    }

    /// <summary>
    /// Returns the count of savings plans for the current user.
    /// </summary>
    [HttpGet("count")]
    public async Task<IActionResult> CountAsync([FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(new { count = await _service.CountAsync(_current.UserId, onlyActive, ct) });

    /// <summary>
    /// Gets a specific savings plan by id if owned by the current user.
    /// </summary>
    /// <param name="id">Savings plan id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with <see cref="SavingsPlanDto"/> or 404 when not found.</returns>
    [HttpGet("{id:guid}", Name = "GetSavingsPlans")]
    public async Task<IActionResult> GetAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.GetAsync(id, _current.UserId, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Returns analysis information for a savings plan.
    /// </summary>
    [HttpGet("{id:guid}/analysis")]
    public async Task<IActionResult> AnalyzeAsync(Guid id, CancellationToken ct)
    {
        var dto = await _service.AnalyzeAsync(id, _current.UserId, ct);
        return Ok(dto);
    }

    /// <summary>
    /// Creates a new savings plan for the current user.
    /// </summary>
    /// <param name="req">Create request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>201 Created with created <see cref="SavingsPlanDto"/>.</returns>
    [HttpPost]
    public async Task<IActionResult> CreateAsync([FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.CreateAsync(_current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);
        return CreatedAtRoute("GetSavingsPlans", new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Updates an existing savings plan owned by the current user.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] SavingsPlanCreateRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        var dto = await _service.UpdateAsync(id, _current.UserId, req.Name, req.Type, req.TargetAmount, req.TargetDate, req.Interval, req.CategoryId, req.ContractNumber, ct);
        return dto == null ? NotFound() : Ok(dto);
    }

    /// <summary>
    /// Archives the specified savings plan.
    /// </summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.ArchiveAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Deletes the specified savings plan.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, _current.UserId, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>
    /// Sets a symbol attachment for the savings plan.
    /// </summary>
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

    /// <summary>
    /// Clears the symbol attachment for the savings plan.
    /// </summary>
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

    /// <summary>
    /// Uploads a symbol file for the savings plan and assigns it.
    /// </summary>
    [HttpPost("{id:guid}/symbol")]
    [RequestSizeLimit(long.MaxValue)]
    public async Task<IActionResult> UploadSymbolAsync(Guid id, [FromForm] IFormFile? file, [FromForm] Guid? categoryId, CancellationToken ct)
    {
        if (file == null) { return BadRequest(new { error = "File required" }); }
        try
        {
            using var stream = file.OpenReadStream();
            var dto = await _attachments.UploadAsync(_current.UserId, AttachmentEntityKind.SavingsPlan, id, stream, file.FileName, file.ContentType ?? "application/octet-stream", categoryId, AttachmentRole.Symbol, ct);
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
}
