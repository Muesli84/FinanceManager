using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services
{
    public sealed class ClassificationTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.ClassifyAllDrafts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ClassificationTaskExecutor> _logger;

        public ClassificationTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<ClassificationTaskExecutor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<IStatementDraftService>();
            var drafts = await draftService.GetOpenDraftsAsync(context.UserId, ct);
            int total = drafts.Count;
            int processed = 0;
            context.ReportProgress(processed, total, "Starting classification...", 0, 0);
            foreach (var draft in drafts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await draftService.ClassifyDraftAsync(draft.Id, ct);
                    processed++;
                    context.ReportProgress(processed, total, $"Classified {processed}/{total}", 0, 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Classification failed for draft {DraftId}", draft.Id);
                    context.ReportProgress(processed, total, $"Error: {ex.Message}", 1, 1);
                }
            }
            context.ReportProgress(processed, total, "Classification completed.", 0, 0);
        }
    }
}
