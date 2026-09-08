using FinanceManager.Application;
using FinanceManager.Application.Demo;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.Services;

/// <summary>
/// Runs demo-data generation as a background task for a freshly registered first user.
/// </summary>
public sealed class DemoDataTaskExecutor : IBackgroundTaskExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DemoDataTaskExecutor> _logger;
    private readonly IStringLocalizer<DemoDataTaskExecutor> _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DemoDataTaskExecutor"/> class.
    /// </summary>
    /// <param name="scopeFactory">Scope factory used to resolve the demo-data service.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="localizer">Localizer used for user-visible progress messages.</param>
    public DemoDataTaskExecutor(
        IServiceScopeFactory scopeFactory,
        ILogger<DemoDataTaskExecutor> logger,
        IStringLocalizer<DemoDataTaskExecutor> localizer)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _localizer = localizer;
    }

    /// <inheritdoc />
    public BackgroundTaskType Type => BackgroundTaskType.CreateDemoData;

    /// <inheritdoc />
    public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var demoDataService = scope.ServiceProvider.GetRequiredService<IDemoDataService>();

        context.ReportProgress(0, 1, _localizer["DD_Start"], 0, 0);

        try
        {
            await demoDataService.CreateDemoDataAsync(context.UserId, createPostings: true, ct);
            context.ReportProgress(1, 1, _localizer["DD_Completed"], 0, 0);
        }
        catch (OperationCanceledException)
        {
            context.ReportProgress(0, 1, _localizer["DD_Canceled"], 0, 0);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo data generation failed for user {UserId}", context.UserId);
            context.ReportProgress(0, 1, _localizer["DD_Failed"], 0, 1);
            throw;
        }
    }
}
