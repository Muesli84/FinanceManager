using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Application.Aggregates;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services
{
    public sealed class RebuildAggregatesTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.RebuildAggregates;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RebuildAggregatesTaskExecutor> _logger;
        private readonly IStringLocalizer<RebuildAggregatesTaskExecutor> _localizer;

        public RebuildAggregatesTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<RebuildAggregatesTaskExecutor> logger, IStringLocalizer<RebuildAggregatesTaskExecutor> localizer)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _localizer = localizer;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var agg = scope.ServiceProvider.GetRequiredService<IPostingAggregateService>();
            var taskMgr = scope.ServiceProvider.GetRequiredService<IBackgroundTaskManager>();

            // report start
            context.ReportProgress(0, null, _localizer["RB_Start"], 0, 0);
            try
            {
                await agg.RebuildForUserAsync(context.UserId, (done, total) =>
                {
                    var prev = taskMgr.Get(context.TaskId);
                    if (prev != null)
                    {
                        var updated = prev with { Processed = done, Total = total, Message = _localizer["RB_Progress", done, total] };
                        taskMgr.UpdateTaskInfo(updated);
                    }
                }, ct);
                context.ReportProgress(1, 1, _localizer["RB_Completed"], 0, 0);
            }
            catch (OperationCanceledException)
            {
                context.ReportProgress(0, 1, _localizer["RB_Canceled"], 0, 0);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rebuild aggregates failed");
                context.ReportProgress(0, 1, ex.Message, 0, 1);
                throw;
            }
        }
    }
}
