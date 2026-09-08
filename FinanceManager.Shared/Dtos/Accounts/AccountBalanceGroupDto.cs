namespace FinanceManager.Shared.Dtos.Accounts;

/// <summary>
/// Aggregated balance information for one account statistics group.
/// </summary>
/// <param name="Key">Stable technical group key.</param>
/// <param name="DisplayName">Optional display name supplied by the server.</param>
/// <param name="NetBalance">Signed group balance.</param>
/// <param name="PositiveBalance">Sum of all positive balances in the group.</param>
/// <param name="NegativeBalanceMagnitude">Absolute sum of all negative balances in the group.</param>
/// <param name="GrossMagnitude">Absolute balance volume used for chart geometry.</param>
/// <param name="AccountCount">Number of accounts in the group.</param>
/// <param name="ZeroBalanceCount">Number of zero-balance accounts in the group.</param>
/// <returns>The result.</returns>
public sealed record AccountBalanceGroupDto(
    string Key,
    string? DisplayName,
    decimal NetBalance,
    decimal PositiveBalance,
    decimal NegativeBalanceMagnitude,
    decimal GrossMagnitude,
    int AccountCount,
    int ZeroBalanceCount);
