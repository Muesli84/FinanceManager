using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Domain.Notifications;

/// <summary>
/// Represents a user or system notification displayed in the UI or scheduled to appear.
/// </summary>
public sealed class Notification
{
    /// <summary>
    /// Unique notification identifier.
    /// </summary>
    /// <returns>The result.</returns>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Optional owner user identifier; when null the notification is global.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>
    /// Short title of the notification.
    /// </summary>
    [MaxLength(140)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed message text.
    /// </summary>
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Notification type used to classify and filter notifications.
    /// </summary>
    public NotificationType Type { get; set; }

    /// <summary>
    /// Target area where the notification should be displayed (homepage, dashboard, modal, toast).
    /// </summary>
    public NotificationTarget Target { get; set; } = NotificationTarget.HomePage;

    /// <summary>
    /// Scheduled date in UTC when the notification becomes active.
    /// </summary>
    public DateTime ScheduledDateUtc { get; set; }

    /// <summary>
    /// Indicates whether the notification is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Indicates whether the notification has been dismissed by the user.
    /// </summary>
    public bool IsDismissed { get; set; } = false;

    /// <summary>
    /// Optional event key to trigger the notification.
    /// </summary>
    [MaxLength(120)]
    public string? TriggerEventKey { get; set; }

    /// <summary>
    /// Creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modification timestamp in UTC.
    /// </summary>
    public DateTime? ModifiedUtc { get; set; }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="Notification"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Unique notification identifier.</param>
    /// <param name="OwnerUserId">Optional owner user identifier; null means global.</param>
    /// <param name="Title">Short title of the notification.</param>
    /// <param name="Message">Detailed message text.</param>
    /// <param name="Type">Notification type used to classify the notification.</param>
    /// <param name="Target">Target area where the notification should be displayed.</param>
    /// <param name="ScheduledDateUtc">Scheduled UTC date when the notification becomes active.</param>
    /// <param name="IsEnabled">Whether the notification is enabled.</param>
    /// <param name="IsDismissed">Whether the notification has been dismissed.</param>
    /// <param name="TriggerEventKey">Optional event key that triggers the notification.</param>
    /// <param name="CreatedUtc">Creation timestamp in UTC.</param>
    /// <param name="ModifiedUtc">Last modification timestamp in UTC, if any.</param>
    /// <returns>The result.</returns>
    public sealed record NotificationBackupDto(Guid Id, Guid? OwnerUserId, string Title, string Message, NotificationType Type, NotificationTarget Target, DateTime ScheduledDateUtc, bool IsEnabled, bool IsDismissed, string? TriggerEventKey, DateTime CreatedUtc, DateTime? ModifiedUtc);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of the notification.
    /// </summary>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="Title">The title.</param>
    /// <param name="Message">The message.</param>
    /// <param name="Type">The type.</param>
    /// <param name="Target">The target.</param>
    /// <param name="ScheduledDateUtc">The scheduled date utc.</param>
    /// <param name="IsEnabled">The is enabled.</param>
    /// <param name="IsDismissed">The is dismissed.</param>
    /// <param name="TriggerEventKey">The trigger event key.</param>
    /// <param name="CreatedUtc">The created utc.</param>
    /// <param name="ModifiedUtc">The modified utc.</param>
    /// <returns>A <see cref="NotificationBackupDto"/> containing the data required to restore this notification.</returns>
    public NotificationBackupDto ToBackupDto() => new NotificationBackupDto(Id, OwnerUserId, Title, Message, Type, Target, ScheduledDateUtc, IsEnabled, IsDismissed, TriggerEventKey, CreatedUtc, ModifiedUtc);

    /// <summary>
    /// Assigns values from a backup DTO to this notification instance.
    /// </summary>
    /// <param name="dto">The <see cref="NotificationBackupDto"/> containing values to apply to this instance.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(NotificationBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Id = dto.Id;
        Title = dto.Title;
        Message = dto.Message;
        Type = dto.Type;
        Target = dto.Target;
        ScheduledDateUtc = dto.ScheduledDateUtc;
        IsEnabled = dto.IsEnabled;
        IsDismissed = dto.IsDismissed;
        TriggerEventKey = dto.TriggerEventKey;
        CreatedUtc = dto.CreatedUtc;
        ModifiedUtc = dto.ModifiedUtc;
    }
}
