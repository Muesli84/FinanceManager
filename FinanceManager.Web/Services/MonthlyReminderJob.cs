using FinanceManager.Domain.Notifications;
using FinanceManager.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using FinanceManager.Application.Notifications;

namespace FinanceManager.Web.Services;

public sealed class MonthlyReminderJob
{
    private readonly IStringLocalizer<MonthlyReminderJob> _localizer;
    private readonly IHolidayProviderResolver _holidayResolver;

    public MonthlyReminderJob(IStringLocalizer<MonthlyReminderJob> localizer, IHolidayProviderResolver holidayResolver)
    {
        _localizer = localizer;
        _holidayResolver = holidayResolver;
    }

    /// <summary>
    /// Schedules monthly reminders considering each user's local time zone (if available).
    /// A reminder is created once per user on that user's last local business day of the month
    /// at or after configured local time (default 09:00). Fallback: server calendar/time if timezone is missing/invalid.
    /// </summary>
    public async Task RunAsync(AppDbContext db, DateTime nowUtc, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(u => u.Active && u.MonthlyReminderEnabled)
            .Select(u => new { u.Id, u.TimeZoneId, u.PreferredLanguage, u.MonthlyReminderHour, u.MonthlyReminderMinute, u.HolidayCountryCode, u.HolidaySubdivisionCode, u.HolidayProviderKind })
            .ToListAsync(ct);
        if (users.Count == 0)
        {
            return;
        }

        foreach (var u in users)
        {
            // Determine user-local now, date and last business day
            TimeZoneInfo? tz = null;
            DateTime userNowLocal;
            try
            {
                if (!string.IsNullOrWhiteSpace(u.TimeZoneId))
                {
                    tz = TimeZoneInfo.FindSystemTimeZoneById(u.TimeZoneId);
                    userNowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
                }
                else
                {
                    userNowLocal = nowUtc; // fallback: treat UTC as local
                }
            }
            catch
            {
                userNowLocal = nowUtc; // fallback on invalid tz
            }

            var userLocalDate = userNowLocal.Date;

            // Determine last business day WITH holidays for user's region if configured
            var lastDayOfMonth = new DateTime(userLocalDate.Year, userLocalDate.Month, DateTime.DaysInMonth(userLocalDate.Year, userLocalDate.Month));
            var cursor = lastDayOfMonth;
            var holidayProvider = _holidayResolver.Resolve(u.HolidayProviderKind);
            while (!BusinessDayCalculator.IsBusinessDay(cursor, holidayProvider, u.HolidayCountryCode, u.HolidaySubdivisionCode))
            {
                cursor = cursor.AddDays(-1);
            }

            if (userLocalDate != cursor.Date)
            {
                continue; // not the user's last business day
            }

            // Configured trigger time (defaults 09:00)
            var targetHour = u.MonthlyReminderHour ?? 9;
            var targetMinute = u.MonthlyReminderMinute ?? 0;
            var targetTime = new TimeSpan(targetHour, targetMinute, 0);

            if (userNowLocal.TimeOfDay < targetTime)
            {
                continue; // not yet time
            }

            // Compute a normalized UTC day key from user's local midnight to ensure idempotency across UTC date boundaries
            DateTime scheduledDateUtc;
            if (tz != null)
            {
                var localMidnight = new DateTime(userLocalDate.Year, userLocalDate.Month, userLocalDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
                try
                {
                    scheduledDateUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz).Date;
                }
                catch
                {
                    scheduledDateUtc = nowUtc.Date; // fallback
                }
            }
            else
            {
                scheduledDateUtc = nowUtc.Date;
            }

            // Idempotency check per user + scheduled UTC day
            bool exists = await db.Notifications.AsNoTracking()
                .AnyAsync(n => n.Type == NotificationType.MonthlyReminder
                               && n.OwnerUserId == u.Id
                               && n.ScheduledDateUtc == scheduledDateUtc, ct);
            if (exists)
            {
                continue;
            }

            // Localize texts using user's preferred language if available
            string title;
            string message;
            var originalCulture = System.Globalization.CultureInfo.CurrentUICulture;
            try
            {
                if (!string.IsNullOrWhiteSpace(u.PreferredLanguage))
                {
                    var cul = new System.Globalization.CultureInfo(u.PreferredLanguage);
                    System.Globalization.CultureInfo.CurrentUICulture = cul;
                }
            }
            catch
            {
                // ignore invalid culture; keep default
            }

            try
            {
                title = _localizer["MonthlyReminder_Title"];
                message = _localizer["MonthlyReminder_Message"];
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentUICulture = originalCulture;
            }

            db.Notifications.Add(new Notification
            {
                OwnerUserId = u.Id,
                Title = title,
                Message = message,
                Type = NotificationType.MonthlyReminder,
                Target = NotificationTarget.HomePage,
                ScheduledDateUtc = scheduledDateUtc,
                IsEnabled = true
            });
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
