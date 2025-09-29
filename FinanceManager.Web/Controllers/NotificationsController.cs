using System.Net.Mime;
using FinanceManager.Application;
using FinanceManager.Application.Notifications;
using FinanceManager.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notifications;
    private readonly ICurrentUserService _current;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(INotificationService notifications, ICurrentUserService current, ILogger<NotificationsController> logger)
    {
        _notifications = notifications;
        _current = current;
        _logger = logger;
    }

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
