namespace FinanceManager.Shared.Dtos.Admin;

/// <summary>
/// DTO representing a user's notification settings.
/// </summary>
public sealed class NotificationSettingsDto
{
    /// <summary>Enables or disables monthly reminder notifications.</summary>
    public bool MonthlyReminderEnabled { get; set; }
    /// <summary>Optional hour for the reminder (0-23).</summary>
    public int? MonthlyReminderHour { get; set; }
    /// <summary>Optional minute for the reminder (0-59).</summary>
    public int? MonthlyReminderMinute { get; set; }
    /// <summary>Holiday provider kind represented as string.</summary>
    public string? HolidayProvider { get; set; } = "Memory";
    /// <summary>Optional ISO country code for holidays.</summary>
    public string? HolidayCountryCode { get; set; }
    /// <summary>Optional region/subdivision code for holidays.</summary>
    public string? HolidaySubdivisionCode { get; set; }
}
