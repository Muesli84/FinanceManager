using FinanceManager.Shared.Dtos;

namespace FinanceManager.Application.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationDto>> ListActiveAsync(Guid ownerUserId, DateTime asOfUtc, CancellationToken ct);
    Task<bool> DismissAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}
