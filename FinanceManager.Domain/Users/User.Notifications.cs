namespace FinanceManager.Domain.Users;

public sealed partial class User
{
    // --- Notification settings ---
    public bool MonthlyReminderEnabled { get; private set; } = false;

    /// <summary>
    /// Local time (user time zone) hour/minute for monthly reminder. Null => default 09:00.
    /// </summary>
    public int? MonthlyReminderHour { get; private set; }
    public int? MonthlyReminderMinute { get; private set; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code for public holiday calendar (e.g., "DE"). Optional.
    /// </summary>
    public string? HolidayCountryCode { get; private set; }

    /// <summary>
    /// ISO 3166-2 subdivision/state code (e.g., "DE-BY"). Optional.
    /// </summary>
    public string? HolidaySubdivisionCode { get; private set; }

    public void SetNotificationSettings(bool monthlyReminderEnabled)
    {
        MonthlyReminderEnabled = monthlyReminderEnabled;
        Touch();
    }

    public void SetMonthlyReminderTime(int? hour, int? minute)
    {
        if (hour.HasValue)
        {
            if (hour.Value < 0 || hour.Value > 23)
            {
                throw new ArgumentOutOfRangeException(nameof(hour), "Hour must be between 0 and 23.");
            }
        }
        if (minute.HasValue)
        {
            if (minute.Value < 0 || minute.Value > 59)
            {
                throw new ArgumentOutOfRangeException(nameof(minute), "Minute must be between 0 and 59.");
            }
        }
        MonthlyReminderHour = hour;
        MonthlyReminderMinute = minute;
        Touch();
    }

    public void SetHolidayRegion(string? countryCode, string? subdivisionCode)
    {
        HolidayCountryCode = Normalize(countryCode, 2, 10);
        HolidaySubdivisionCode = Normalize(subdivisionCode, 2, 20);
        Touch();

        static string? Normalize(string? s, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) { return null; }
            var v = s.Trim();
            if (v.Length < min || v.Length > max) { throw new ArgumentOutOfRangeException(nameof(s)); }
            return v.ToUpperInvariant();
        }
    }

    public FinanceManager.Domain.Notifications.HolidayProviderKind HolidayProviderKind { get; private set; } = FinanceManager.Domain.Notifications.HolidayProviderKind.Memory;

    public void SetHolidayProvider(FinanceManager.Domain.Notifications.HolidayProviderKind kind)
    {
        HolidayProviderKind = kind;
        Touch();
    }
}
