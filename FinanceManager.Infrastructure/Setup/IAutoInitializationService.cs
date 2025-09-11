namespace FinanceManager.Infrastructure.Setup
{
    public interface IAutoInitializationService
    {
        void Run();
        Task RunAsync(CancellationToken ct);
    }
}
