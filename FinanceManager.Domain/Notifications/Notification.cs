using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Domain.Notifications;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Owner scope: optional global notifications (null) vs per user
    public Guid? OwnerUserId { get; set; }

    [MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    public NotificationTarget Target { get; set; } = NotificationTarget.HomePage;

    // When to show (local date recommended; store as UTC date without time for scheduling simplicity)
    public DateTime ScheduledDateUtc { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsDismissed { get; set; } = false;

    // Optional event key for event-driven notifications
    [MaxLength(120)]
    public string? TriggerEventKey { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedUtc { get; set; }
}
