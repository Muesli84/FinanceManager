namespace FinanceManager.Shared.Extensions
{
    /// <summary>
    /// Extensions methods for string.
    /// </summary>
    public static class StringExt
    {
        /// <summary>
        /// Converts the string to int32. Returns default(int) if conversion fails.
        /// </summary>
        /// <param name="value">string value</param>
        /// <returns></returns>
        public static int ToInt32(this string value)
        {
            if (int.TryParse(value, out var result))
                return result;
            return default(int);
        }
    }
}
