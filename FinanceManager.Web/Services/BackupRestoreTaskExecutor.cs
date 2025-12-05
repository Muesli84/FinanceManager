using FinanceManager.Application;
using FinanceManager.Application.Backups;
using Microsoft.Extensions.Localization;
using System.Text.Json;

namespace FinanceManager.Web.Services
{
    public sealed class BackupRestoreTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.BackupRestore;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackupRestoreTaskExecutor> _logger;
        private readonly IStringLocalizer<BackupRestoreTaskExecutor> _localizer;

        private sealed record RestorePayload(Guid BackupId, bool ReplaceExisting);

        public BackupRestoreTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<BackupRestoreTaskExecutor> logger, IStringLocalizer<BackupRestoreTaskExecutor> localizer)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _localizer = localizer;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            if (context.Payload is not string raw || string.IsNullOrWhiteSpace(raw))
            {
                context.ReportProgress(0, 1, _localizer["BR_NoBackupId"], 0, 1);
                throw new InvalidOperationException("BackupId payload required");
            }
            Guid backupId;
            var replaceExisting = true; // current behaviour always replace
            try
            {
                using var doc = JsonDocument.Parse(raw);
                backupId = doc.RootElement.GetProperty("BackupId").GetGuid();
                if (doc.RootElement.TryGetProperty("ReplaceExisting", out var repEl))
                {
                    replaceExisting = repEl.GetBoolean();
                }
            }
            catch (Exception ex)
            {
                context.ReportProgress(0, 1, _localizer["BR_InvalidPayload"], 0, 1);
                throw new InvalidOperationException("Invalid backup restore payload", ex);
            }

            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var taskManager = scope.ServiceProvider.GetRequiredService<IBackgroundTaskManager>();

            context.ReportProgress(0, 0, _localizer["BR_Start"], 0, 0);
            try
            {
                var ok = await backupService.ApplyAsync(context.UserId, backupId, (desc, step, stepTotal, subStep, subTotal) =>
                {
                    var info = taskManager.Get(context.TaskId);
                    if (info != null)
                    {
                        var updated = info with
                        {
                            Processed = step + 1,
                            Total = stepTotal + 1,
                            Message = string.IsNullOrWhiteSpace(desc) ? info.Message : desc,
                            Processed2 = subStep,
                            Total2 = subTotal,
                            Message2 = null
                        };
                        taskManager.UpdateTaskInfo(updated);
                    }
                }, ct);
                if (!ok)
                {
                    context.ReportProgress(0, 0, _localizer["BR_ApplyFailed"], 0, 1);
                    throw new InvalidOperationException("Backup apply failed");
                }
                var finalInfo = taskManager.Get(context.TaskId);
                var finalProcessed = finalInfo?.Processed ?? 1;
                var finalTotal = finalInfo?.Total ?? finalProcessed;
                context.ReportProgress(finalProcessed, finalTotal, _localizer["BR_Completed"], 0, 0);
            }
            catch (OperationCanceledException)
            {
                context.ReportProgress(0, 0, _localizer["BR_Canceled"], 0, 0);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup restore failed for backup {BackupId}", backupId);
                context.ReportProgress(0, 0, ex.Message, 0, 1);
                throw;
            }
        }
    }
}
