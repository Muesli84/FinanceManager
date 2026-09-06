namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Represents a deterministic monthly period key.
/// </summary>
/// <param name="Year">The year.</param>
/// <param name="Month">The month.</param>
/// <returns>The result.</returns>
public readonly record struct BudgetPeriodKey(int Year, int Month)
{
    /// <summary>
    /// Creates a <see cref="BudgetPeriodKey"/> from a date.
    /// </summary>
    /// <param name="date">Date to convert.</param>
    /// <returns>Period key for the month.</returns>
    public static BudgetPeriodKey FromDate(DateOnly date) => new(date.Year, date.Month);

    /// <summary>
    /// Returns the first day of the represented month.
    /// </summary>
    /// <param name="Year">The year.</param>
    /// <param name="Month">The month.</param>
    /// <returns>The result.</returns>
    public DateOnly StartDate => new(Year, Month, 1);

    /// <summary>
    /// Returns the last day of the represented month.
    /// </summary>
    /// <param name="Year">The year.</param>
    /// <param name="Month">The month.</param>
    /// <returns>The result.</returns>
    public DateOnly EndDate => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    /// <summary>
    /// Adds months to this period key.
    /// </summary>
    /// <param name="months">Months to add.</param>
    /// <returns>New period key.</returns>
    public BudgetPeriodKey AddMonths(int months)
    {
        var dt = new DateOnly(Year, Month, 1).AddMonths(months);
        return new BudgetPeriodKey(dt.Year, dt.Month);
    }

    /// <summary>
    /// Validates the period key.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when month is outside 1..12.</exception>
    public void Validate()
    {
        if (Month < 1 || Month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(Month), "Month must be between 1 and 12");
        }
    }

    /// <summary>
    /// Returns YYYY-MM representation.
    /// </summary>
    /// <returns>The result.</returns>
    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
