using FinanceManager.Domain.Notifications;

namespace FinanceManager.Application.Notifications;

public interface INotificationWriter
{
    Task CreateForUserAsync(Guid ownerUserId, string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct);
    Task CreateForAdminsAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct);
    Task CreateGlobalAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, CancellationToken ct);
}
