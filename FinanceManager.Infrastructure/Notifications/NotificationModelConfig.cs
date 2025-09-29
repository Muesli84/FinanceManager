using FinanceManager.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Notifications;

public static class NotificationModelConfig
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.OwnerUserId, x.Type, x.ScheduledDateUtc });
            b.Property(x => x.Title).HasMaxLength(140);
            b.Property(x => x.Message).HasMaxLength(1000);
            b.Property(x => x.TriggerEventKey).HasMaxLength(120);
            b.Property(x => x.Type).HasConversion<int>().IsRequired();
            b.Property(x => x.Target).HasConversion<int>().IsRequired();
            b.Property(x => x.ScheduledDateUtc).IsRequired();
            b.Property(x => x.IsEnabled).IsRequired();
            b.Property(x => x.IsDismissed).IsRequired();
            b.Property(x => x.CreatedUtc).IsRequired();
        });
    }
}
