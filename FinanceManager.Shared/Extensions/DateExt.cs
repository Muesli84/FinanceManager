namespace FinanceManager.Shared.Extensions
{
    /// <summary>
    /// Extension methods for DateTime.
    /// </summary>
    public static class DateExt
    {
        /// <summary>
        /// Returns a new DateTime representing the first day of the month of the given date.
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static DateTime ToFirstOfMonth(this DateTime date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }
        /// <summary>
        /// Returns a new DateTime representing the last day of the month of the given date.
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        public static DateTime ToLastOfMonth(this DateTime date)
        {
            int lastDay = DateTime.DaysInMonth(date.Year, date.Month);
            return new DateTime(date.Year, date.Month, lastDay);
        }
    }
}
