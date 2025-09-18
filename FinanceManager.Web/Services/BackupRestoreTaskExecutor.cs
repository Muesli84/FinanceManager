using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Infrastructure.Backups;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services
{
    public sealed class BackupRestoreTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.BackupRestore;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BackupRestoreTaskExecutor> _logger;

        public BackupRestoreTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<BackupRestoreTaskExecutor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupRestoreService>();
            context.ReportProgress(0, null, "Starting restore...", 0, 0);
            try
            {
                await backupService.RestoreAsync(context.Payload, progress =>
                {
                    // progress: (processed, total, message, warnings, errors)
                    context.ReportProgress(progress.Processed, progress.Total, progress.Message, progress.Warnings, progress.Errors);
                }, ct);
                context.ReportProgress(1, 1, "Restore completed.", 0, 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Restore failed");
                context.ReportProgress(0, null, $"Error: {ex.Message}", 0, 1);
                throw;
            }
        }
    }
}
