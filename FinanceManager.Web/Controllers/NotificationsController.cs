using FinanceManager.Application;
using FinanceManager.Application.Notifications;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers;

/// <summary>
/// Provides endpoints for listing active notifications and dismissing a notification for the current user.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _current;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(INotificationService notifications, ICurrentUserService current, ILogger<NotificationsController> logger)
    { _notifications = notifications; _current = current; _logger = logger; }

    /// <summary>
    /// Lists currently active notifications for the signed-in user (filtered by current UTC time).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try { var data = await _notifications.ListActiveAsync(_current.UserId, DateTime.UtcNow, ct); return Ok(data); }
        catch (Exception ex) { _logger.LogError(ex, "List notifications failed"); return Problem("Unexpected error", statusCode: 500); }
    }

    /// <summary>
    /// Dismisses a notification by id for the current user.
    /// </summary>
    /// <param name="id">Notification id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DismissAsync(Guid id, CancellationToken ct)
    {
        try { var ok = await _notifications.DismissAsync(id, _current.UserId, ct); return ok ? NoContent() : NotFound(); }
        catch (Exception ex) { _logger.LogError(ex, "Dismiss notification {NotificationId} failed", id); return Problem("Unexpected error", statusCode: 500); }
    }
}
