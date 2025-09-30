using FinanceManager.Application.Notifications;
using FinanceManager.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Notifications;

public sealed class NotificationWriter : INotificationWriter
{
    private readonly AppDbContext _db;
    private readonly ILogger<NotificationWriter> _logger;

    public NotificationWriter(AppDbContext db, ILogger<NotificationWriter> logger)
    {
        _db = db; _logger = logger;
    }

    public async Task CreateForUserAsync(Guid ownerUserId, string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct)
    {
        try
        {
            _db.Notifications.Add(new Notification
            {
                OwnerUserId = ownerUserId,
                Title = title,
                Message = message,
                Type = type,
                Target = target,
                ScheduledDateUtc = scheduledDateUtc,
                IsEnabled = true,
                IsDismissed = false,
                CreatedUtc = DateTime.UtcNow,
                TriggerEventKey = triggerEventKey
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create notification for user {UserId}", ownerUserId);
        }
    }

    public async Task CreateForAdminsAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, string? triggerEventKey, CancellationToken ct)
    {
        try
        {
            var admins = await _db.Users.AsNoTracking().Where(u => u.IsAdmin && u.Active).Select(u => u.Id).ToListAsync(ct);
            if (admins.Count == 0) return;
            foreach (var id in admins)
            {
                _db.Notifications.Add(new Notification
                {
                    OwnerUserId = id,
                    Title = title,
                    Message = message,
                    Type = type,
                    Target = target,
                    ScheduledDateUtc = scheduledDateUtc,
                    IsEnabled = true,
                    IsDismissed = false,
                    CreatedUtc = DateTime.UtcNow,
                    TriggerEventKey = triggerEventKey,
                });
            }
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create admin notifications");
        }
    }

    public async Task CreateGlobalAsync(string title, string message, NotificationType type, NotificationTarget target, DateTime scheduledDateUtc, CancellationToken ct)
    {
        try
        {
            _db.Notifications.Add(new Notification
            {
                OwnerUserId = null,
                Title = title,
                Message = message,
                Type = type,
                Target = target,
                ScheduledDateUtc = scheduledDateUtc,
                IsEnabled = true,
                IsDismissed = false,
                CreatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create global notification");
        }
    }
}
