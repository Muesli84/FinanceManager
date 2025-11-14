using FinanceManager.Application;
using FinanceManager.Application.Notifications;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;

namespace FinanceManager.Web.Controllers; // moved from .Shared to Controllers for test compatibility

/// <summary>
/// Endpoints to list and dismiss user notifications.
/// Delegates operations to <see cref="INotificationService"/> and uses <see cref="ICurrentUserService"/> to determine the calling user.
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

    /// <summary>
    /// Creates a new instance of <see cref="NotificationsController"/>.
    /// </summary>
    /// <param name="notifications">Notification service.</param>
    /// <param name="current">Current user service.</param>
    /// <param name="logger">Logger instance.</param>
    public NotificationsController(INotificationService notifications, ICurrentUserService current, ILogger<NotificationsController> logger)
    {
        _notifications = notifications;
        _current = current;
        _logger = logger;
    }

    /// <summary>
    /// Lists active notifications for the current user as of now.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with a list of <see cref="NotificationDto"/> or 500 on unexpected error.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(CancellationToken ct)
    {
        try
        {
            var data = await _notifications.ListActiveAsync(_current.UserId, DateTime.UtcNow, ct);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List notifications failed");
            return Problem("Unexpected error", statusCode: 500);
        }
    }

    /// <summary>
    /// Dismisses a notification for the current user.
    /// </summary>
    /// <param name="id">Notification identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content when dismissed, 404 if not found, 500 on unexpected error.</returns>
    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DismissAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var ok = await _notifications.DismissAsync(id, _current.UserId, ct);
            return ok ? NoContent() : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dismiss notification {NotificationId} failed", id);
            return Problem("Unexpected error", statusCode: 500);
        }
    }
}

