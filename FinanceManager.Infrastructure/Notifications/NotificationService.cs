using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Notifications;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;

    public NotificationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<NotificationDto>> ListActiveAsync(Guid ownerUserId, DateTime asOfUtc, CancellationToken ct)
    {
        var nowDateUtc = asOfUtc.Date;
        var items = await _db.Notifications.AsNoTracking()
            .Where(n => (n.OwnerUserId == ownerUserId || n.OwnerUserId == null)
                        && n.IsEnabled
                        && !n.IsDismissed
                        && n.ScheduledDateUtc <= nowDateUtc)
            .OrderByDescending(n => n.ScheduledDateUtc)
            .ThenByDescending(n => n.CreatedUtc)
            .Select(n => new NotificationDto(n.Id, n.Title, n.Message, (int)n.Type, (int)n.Target, n.ScheduledDateUtc, n.IsDismissed, n.CreatedUtc, n.TriggerEventKey))
            .ToListAsync(ct);
        return items;
    }

    public async Task<bool> DismissAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && (n.OwnerUserId == ownerUserId || n.OwnerUserId == null), ct);
        if (entity is null)
        {
            return false;
        }
        entity.IsDismissed = true;
        entity.ModifiedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
