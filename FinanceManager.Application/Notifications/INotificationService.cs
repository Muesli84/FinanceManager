namespace FinanceManager.Application.Notifications;

/// <summary>
/// Service to list and dismiss notifications for a user.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Lists active notifications for the owner at the specified time.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="asOfUtc">Point in time to evaluate active notifications (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<NotificationDto>> ListActiveAsync(Guid ownerUserId, DateTime asOfUtc, CancellationToken ct);

    /// <summary>
    /// Dismisses a notification for the owner.
    /// </summary>
    /// <param name="id">Notification id.</param>
    /// <param name="ownerUserId">Owner user id requesting dismissal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DismissAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}
