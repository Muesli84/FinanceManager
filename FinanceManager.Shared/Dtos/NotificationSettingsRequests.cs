using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to update user notification settings such as monthly reminder time and holiday region.
/// </summary>
/// <param name="MonthlyReminderEnabled">Enables or disables monthly reminder notifications.</param>
/// <param name="MonthlyReminderHour">Optional hour for the reminder (0-23).</param>
/// <param name="MonthlyReminderMinute">Optional minute for the reminder (0-59).</param>
/// <param name="HolidayProvider">The holiday provider kind as string.</param>
/// <param name="HolidayCountryCode">Optional ISO country code.</param>
/// <param name="HolidaySubdivisionCode">Optional region/subdivision code.</param>
public sealed record UserNotificationSettingsUpdateRequest(
    bool MonthlyReminderEnabled,
    [property: Range(0,23)] int? MonthlyReminderHour,
    [property: Range(0,59)] int? MonthlyReminderMinute,
    [property: Required] string HolidayProvider,
    [property: StringLength(10, MinimumLength = 2)] string? HolidayCountryCode,
    [property: StringLength(20, MinimumLength = 2)] string? HolidaySubdivisionCode
);
