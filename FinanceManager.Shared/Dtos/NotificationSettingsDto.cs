namespace FinanceManager.Shared.Dtos;

public sealed class NotificationSettingsDto
{
    public bool MonthlyReminderEnabled { get; set; }
    public int? MonthlyReminderHour { get; set; }
    public int? MonthlyReminderMinute { get; set; }
    public string? HolidayProvider { get; set; } // "Memory" or "NagerDate"
    public string? HolidayCountryCode { get; set; }
    public string? HolidaySubdivisionCode { get; set; }
}
