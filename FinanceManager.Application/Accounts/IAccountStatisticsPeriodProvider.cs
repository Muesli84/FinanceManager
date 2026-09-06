namespace FinanceManager.Application.Accounts;

/// <summary>
/// Provides local calendar boundaries for account statistics.
/// </summary>
public interface IAccountStatisticsPeriodProvider
{
    /// <summary>
    /// Resolves the current local account statistics period for the specified owner.
    /// </summary>
    Task<AccountStatisticsPeriod> GetPeriodAsync(Guid ownerUserId, CancellationToken ct);
}
