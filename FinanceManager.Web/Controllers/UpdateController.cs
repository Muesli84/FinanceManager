using FinanceManager.Shared.Dtos.Common;
using FinanceManager.Shared.Dtos.Update;
using FinanceManager.Web.Services.Updates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Admin-only API that exposes the update subsystem: status, settings, checks, scheduling and installation.
/// </summary>
[ApiController]
[Route("api/setup/update")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
public sealed class UpdateController : ControllerBase
{
    private const string Origin = "API_Update";
    private readonly IUpdateOrchestrator _orchestrator;
    private readonly IUpdateServiceCatalog _serviceCatalog;
    private readonly ILogger<UpdateController> _logger;

    /// <summary>
    /// Creates a new <see cref="UpdateController"/>.
    /// </summary>
    /// <param name="orchestrator">Update orchestrator used for status, settings and install operations.</param>
    /// <param name="serviceCatalog">Catalog of updatable service names.</param>
    /// <param name="logger">Logger instance.</param>
    public UpdateController(IUpdateOrchestrator orchestrator, IUpdateServiceCatalog serviceCatalog, ILogger<UpdateController> logger)
    {
        _orchestrator = orchestrator;
        _serviceCatalog = serviceCatalog;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current update status.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current <see cref="UpdateStatusDto"/>.</returns>
    /// <response code="200">Current update status.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(UpdateStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
        => Ok(await _orchestrator.GetStatusAsync(ct));

    /// <summary>
    /// Returns the current update settings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current <see cref="UpdateSettingsDto"/>.</returns>
    /// <response code="200">Current update settings.</response>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(UpdateSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken ct)
        => Ok(await _orchestrator.GetSettingsAsync(ct));

    /// <summary>
    /// Saves new update settings.
    /// </summary>
    /// <param name="request">Settings update request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="UpdateSettingsDto"/>.</returns>
    /// <response code="200">Settings were saved.</response>
    [HttpPut("settings")]
    [ProducesResponseType(typeof(UpdateSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateSettingsUpdateRequest request, CancellationToken ct)
        => Ok(await _orchestrator.SaveSettingsAsync(request, ct));

    /// <summary>
    /// Lists known service names that can be updated.
    /// </summary>
    /// <param name="query">Optional case-insensitive substring filter.</param>
    /// <param name="take">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of service names.</returns>
    /// <response code="200">List of service names.</response>
    [HttpGet("services")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Services([FromQuery] string? query, [FromQuery] int take = 20, CancellationToken ct = default)
        => Ok(await _serviceCatalog.ListServiceNamesAsync(query, take, ct));

    /// <summary>
    /// Triggers an update check.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="UpdateCheckResultDto"/> result.</returns>
    /// <response code="200">Result of the update check.</response>
    [HttpPost("check")]
    [ProducesResponseType(typeof(UpdateCheckResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Check(CancellationToken ct)
        => Ok(await _orchestrator.CheckAsync(ct));

    /// <summary>
    /// Schedules the automatic update installation time.
    /// </summary>
    /// <param name="request">Schedule request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="UpdateSettingsDto"/>.</returns>
    /// <response code="200">Schedule was applied.</response>
    [HttpPost("schedule")]
    [ProducesResponseType(typeof(UpdateSettingsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Schedule([FromBody] UpdateScheduleRequest request, CancellationToken ct)
        => Ok(await _orchestrator.ScheduleAsync(request.ScheduledInstallTime, ct));

    /// <summary>
    /// Starts the update installation.
    /// </summary>
    /// <param name="request">Install start request including the downtime confirmation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resulting <see cref="UpdateStatusDto"/>.</returns>
    /// <response code="200">Installation started.</response>
    /// <response code="400">The request or current update state is invalid.</response>
    /// <response code="404">No downloaded update package is ready.</response>
    /// <response code="409">The update subsystem is locked by another operation.</response>
    [HttpPost("install/start")]
    [ProducesResponseType(typeof(UpdateStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StartInstall([FromBody] UpdateStartRequest request, CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Update installation requested by {User}. ConfirmDowntime: {ConfirmDowntime}", User.Identity?.Name, request.ConfirmDowntime);
            return Ok(await _orchestrator.StartInstallAsync(request.ConfirmDowntime, ct));
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Update installation failed: {Message}", ex.Message);
            return NotFound(ApiErrorDto.Create(Origin, "Err_Update_NotReady", ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Update installation failed: {Message}", ex.Message);
            return Conflict(ApiErrorDto.Create(Origin, "Err_Update_Locked", ex.Message));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Update installation failed: {Message}", ex.Message);
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Update_InvalidRequest", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Update installation failed: {Message}", ex.Message);
            return BadRequest(ApiErrorDto.Create(Origin, "Err_Update_InvalidState", ex.Message));
        }
    }

    /// <summary>
    /// Resets a stale update lock so a new update operation can be started.
    /// </summary>
    /// <param name="request">Lock reset request with an optional reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>No content on success; an <see cref="ApiErrorDto"/> on failure.</returns>
    /// <response code="204">The lock was reset.</response>
    /// <response code="409">No lock exists or the lock is not stale.</response>
    /// <response code="500">Resetting the lock failed.</response>
    [HttpPost("lock/reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorDto), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetLock([FromBody] UpdateLockResetRequest request, CancellationToken ct)
    {
        try
        {
            var sanitizedReason = request.Reason?
                .Replace("\r", " ")
                .Replace("\n", " ");

            _logger.LogWarning("Update lock reset requested by {User}. Reason: {Reason}", User.Identity?.Name, sanitizedReason);
            await _orchestrator.ResetLockAsync(request.Reason, ct);
            return NoContent();
        }
        catch (UpdateLockResetException ex)
        {
            var statusCode = MapResetFailureStatusCode(ex.Kind);
            var errorCode = MapResetFailureCode(ex.Kind);
            LogResetFailure(ex);
            return StatusCode(statusCode, ApiErrorDto.Create(Origin, errorCode, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Update lock reset failed with an unclassified I/O error: {Message}", ex.Message);
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiErrorDto.Create(Origin, "Err_Update_Reset_Failed", ex.Message));
        }
    }

    private static int MapResetFailureStatusCode(UpdateLockResetFailureKind kind)
        => kind == UpdateLockResetFailureKind.ResetFailed
            ? StatusCodes.Status500InternalServerError
            : StatusCodes.Status409Conflict;

    private static string MapResetFailureCode(UpdateLockResetFailureKind kind)
        => kind switch
        {
            UpdateLockResetFailureKind.NoLock => "Err_Update_Reset_NoLock",
            UpdateLockResetFailureKind.LockNotStale => "Err_Update_Reset_LockNotStale",
            UpdateLockResetFailureKind.LockDeleteFailed => "Err_Update_Reset_DeleteFailed",
            _ => "Err_Update_Reset_Failed"
        };

    private void LogResetFailure(UpdateLockResetException ex)
    {
        var logLevel = ex.Kind == UpdateLockResetFailureKind.ResetFailed ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(
            logLevel,
            ex,
            "Update lock reset failed. Kind: {Kind}; Source: {FailureSource}; LockCreatedAt: {LockCreatedAt}; LockPath: {LockPath}; User: {User}; Message: {Message}",
            ex.Kind,
            ex.FailureSource,
            ex.LockCreatedAt,
            ex.LockPath,
            User.Identity?.Name,
            ex.Message);
    }
}
