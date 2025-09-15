namespace FinanceManager.Web.Services;

public interface IPriceProvider
{
    Task<IReadOnlyList<(DateTime date, decimal close)>> GetDailyPricesAsync(string symbol, DateTime startDateExclusive, DateTime endDateInclusive, CancellationToken ct);
}