namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Bank account overview statistics for the current filtered account scope.
/// </summary>
/// <param name="AccountCount">Number of accounts in scope.</param>
/// <param name="TotalBalance">Current signed total balance.</param>
/// <param name="YearToDateChange">Signed sum of bank postings in the current local calendar year to date.</param>
/// <param name="MonthToDateChange">Signed sum of bank postings in the current local calendar month to date.</param>
/// <param name="TotalGrossMagnitude">Absolute balance volume used as chart percentage denominator.</param>
/// <param name="ByAccountType">Groups by technical account type key.</param>
/// <param name="ByBankContact">Groups by bank contact id or fallback key.</param>
/// <returns>The result.</returns>
public sealed record AccountStatisticsDto(
    int AccountCount,
    decimal TotalBalance,
    decimal YearToDateChange,
    decimal MonthToDateChange,
    decimal TotalGrossMagnitude,
    IReadOnlyList<AccountBalanceGroupDto> ByAccountType,
    IReadOnlyList<AccountBalanceGroupDto> ByBankContact)
{
    /// <summary>
    /// Defined empty statistics payload.
    /// </summary>
    /// <returns>The result.</returns>
    public static AccountStatisticsDto Empty { get; } = new(
        0,
        0m,
        0m,
        0m,
        0m,
        Array.Empty<AccountBalanceGroupDto>(),
        Array.Empty<AccountBalanceGroupDto>());
}
