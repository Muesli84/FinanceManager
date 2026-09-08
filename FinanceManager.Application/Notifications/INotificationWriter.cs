using FinanceManager.Domain.Notifications;

namespace FinanceManager.Application.Notifications;

/// <summary>
/// Helper used to create notifications for users, admins or global audiences.
/// </summary>
public interface INotificationWriter
{
    /// <summary>
    /// Creates a notification for a specific user.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <param name="type">The type.</param>
    /// <param name="target">The target.</param>
    /// <param name="scheduledDateUtc">The scheduled date utc.</param>
    /// <param name="triggerEventKey">The trigger event key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateForUserAsync(Guid ownerUserId, string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct);

    /// <summary>
    /// Creates a notification targeted at administrators.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <param name="type">The type.</param>
    /// <param name="target">The target.</param>
    /// <param name="scheduledDateUtc">The scheduled date utc.</param>
    /// <param name="triggerEventKey">The trigger event key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateForAdminsAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct);

    /// <summary>
    /// Creates a global notification visible to all users.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <param name="type">The type.</param>
    /// <param name="target">The target.</param>
    /// <param name="scheduledDateUtc">The scheduled date utc.</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateGlobalAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, CancellationToken ct);
}
