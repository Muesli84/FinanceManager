namespace FinanceManager.Web.Services;

public static class BusinessDayCalculator
{
    // Simple: weekends only. Extend with holiday calendar later.
    public static DateTime GetLastBusinessDayUtc(DateTime utcNow)
        => GetLastBusinessDay(utcNow);

    /// <summary>
    /// Returns the last business day (Mon-Fri) of the month for the given calendar date.
    /// The input is treated as a calendar date (no timezone conversion); Kind is ignored.
    /// </summary>
    public static DateTime GetLastBusinessDay(DateTime date)
    {
        var monthStart = new DateTime(date.Year, date.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var last = nextMonth.AddDays(-1);
        while (last.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            last = last.AddDays(-1);
        }
        return last.Date;
    }

    /// <summary>
    /// Determines if a date is a business day given a holiday provider and optional region codes.
    /// Weekend (Sat/Sun) are non-business days. Holidays provided by IHolidayProvider are non-business.
    /// </summary>
    public static bool IsBusinessDay(DateTime date, FinanceManager.Application.Notifications.IHolidayProvider? holidays, string? countryCode = null, string? subdivisionCode = null)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }
        if (holidays != null && holidays.IsPublicHoliday(date, countryCode, subdivisionCode))
        {
            return false;
        }
        return true;
    }
}
