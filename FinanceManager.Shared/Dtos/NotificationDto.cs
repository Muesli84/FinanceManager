namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO representing a single notification to be shown to the user.
/// </summary>
/// <param name="Id">Unique identifier for the notification.</param>
/// <param name="Title">Title of the notification.</param>
/// <param name="Message">Localized message text.</param>
/// <param name="Type">Numerical representation of the notification type.</param>
/// <param name="Target">Target audience or recipient of the notification.</param>
/// <param name="ScheduledDateUtc">UTC date and time when the notification is scheduled to be sent.</param>
/// <param name="IsDismissed">Indicates whether the notification has been dismissed by the user.</param>
/// <param name="CreatedUtc">UTC timestamp when the notification was created.</param>
/// <param name="TriggerEventKey">Optional event key to drive UI actions/links.</param>
public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    int Type,
    int Target,
    DateTime ScheduledDateUtc,
    bool IsDismissed,
    DateTime CreatedUtc,
    string? TriggerEventKey // optional event key to drive UI actions/links
);
