namespace FinanceManager.Shared.Dtos;

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
