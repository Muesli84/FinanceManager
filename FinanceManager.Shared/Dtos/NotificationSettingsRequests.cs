using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record UserNotificationSettingsUpdateRequest(
    bool MonthlyReminderEnabled,
    [property: Range(0,23)] int? MonthlyReminderHour,
    [property: Range(0,59)] int? MonthlyReminderMinute,
    [property: Required] string HolidayProvider,
    [property: StringLength(10, MinimumLength = 2)] string? HolidayCountryCode,
    [property: StringLength(20, MinimumLength = 2)] string? HolidaySubdivisionCode
);
