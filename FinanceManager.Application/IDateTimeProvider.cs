namespace FinanceManager.Application;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
