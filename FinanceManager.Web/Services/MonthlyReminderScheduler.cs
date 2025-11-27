using FinanceManager.Infrastructure;

namespace FinanceManager.Web.Services;

public sealed class MonthlyReminderScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonthlyReminderScheduler> _logger;

    public MonthlyReminderScheduler(IServiceScopeFactory scopeFactory, ILogger<MonthlyReminderScheduler> logger)
    {
        _scopeFactory = scopeFactory; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var job = scope.ServiceProvider.GetRequiredService<MonthlyReminderJob>();
                await job.RunAsync(db, DateTime.UtcNow, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonthlyReminderScheduler run failed");
            }

            // run every hour at minute 5 to reduce contention (rough schedule)
            var delay = TimeSpan.FromMinutes(65 - DateTime.UtcNow.Minute % 60);
            try { await Task.Delay(delay, stoppingToken); } catch { }
        }
    }
}
