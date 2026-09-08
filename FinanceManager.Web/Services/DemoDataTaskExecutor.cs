using FinanceManager.Application;
using FinanceManager.Application.Demo;

namespace FinanceManager.Web.Services;

/// <summary>
/// Runs demo-data generation as a background task for a freshly registered first user.
/// </summary>
public sealed class DemoDataTaskExecutor : IBackgroundTaskExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DemoDataTaskExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoDataTaskExecutor"/> class.
    /// </summary>
    /// <param name="scopeFactory">Scope factory used to resolve the demo-data service.</param>
    /// <param name="logger">Logger instance.</param>
    public DemoDataTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<DemoDataTaskExecutor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public BackgroundTaskType Type => BackgroundTaskType.CreateDemoData;

    /// <inheritdoc />
    public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var demoDataService = scope.ServiceProvider.GetRequiredService<IDemoDataService>();

        context.ReportProgress(0, 1, "Demo-Daten werden angelegt...", 0, 0);

        try
        {
            await demoDataService.CreateDemoDataAsync(context.UserId, createPostings: true, ct);
            context.ReportProgress(1, 1, "Demo-Daten wurden angelegt.", 0, 0);
        }
        catch (OperationCanceledException)
        {
            context.ReportProgress(0, 1, "Demo-Daten-Anlage abgebrochen.", 0, 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo data generation failed for user {UserId}", context.UserId);
            context.ReportProgress(0, 1, ex.Message, 0, 1);
            throw;
        }
    }
}
