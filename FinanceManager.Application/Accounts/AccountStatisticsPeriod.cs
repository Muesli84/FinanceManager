namespace FinanceManager.Application.Accounts;

/// <summary>
/// Local period boundaries used for account statistics.
/// </summary>
/// <param name="Today">Current local date at midnight.</param>
/// <param name="YearStart">Current local calendar year start.</param>
/// <param name="MonthStart">Current local calendar month start.</param>
/// <param name="TomorrowExclusive">Exclusive upper bound at the next local midnight.</param>
/// <returns>The result.</returns>
public sealed record AccountStatisticsPeriod(DateTime Today, DateTime YearStart, DateTime MonthStart, DateTime TomorrowExclusive);
