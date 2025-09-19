using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;

namespace FinanceManager.Web.Services
{
    public sealed class ClassificationTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.ClassifyAllDrafts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ClassificationTaskExecutor> _logger;
        private readonly IStringLocalizer<ClassificationTaskExecutor> _localizer;

        public ClassificationTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<ClassificationTaskExecutor> logger, IStringLocalizer<ClassificationTaskExecutor> localizer)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _localizer = localizer;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<IStatementDraftService>();
            var drafts = await draftService.GetOpenDraftsAsync(context.UserId, ct);
            int total = drafts.Count;
            int processed = 0;
            context.ReportProgress(processed, total, _localizer["CL_Start"], 0, 0);
            foreach (var draft in drafts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await draftService.ClassifyAsync(draft.DraftId, null, context.UserId, ct);
                    processed++;
                    context.ReportProgress(processed, total, _localizer["CL_Progress", processed, total], 0, 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Classification failed for draft {DraftId}", draft.DraftId);
                    context.ReportProgress(processed, total, _localizer["CL_Error", ex.Message], 1, 1);
                }
            }
            context.ReportProgress(processed, total, _localizer["CL_Completed"], 0, 0);
        }
    }
}
