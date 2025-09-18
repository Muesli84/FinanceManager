using System;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Statements;
using FinanceManager.Shared.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services
{
    public sealed class BookingTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.BookAllDrafts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingTaskExecutor> _logger;

        public BookingTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<BookingTaskExecutor> logger)
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
            int warnings = 0;
            int errors = 0;
            context.ReportProgress(processed, total, "Starting booking...", warnings, errors);
            foreach (var draft in drafts)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await draftService.BookDraftAsync(draft.Id, ct);
                    processed++;
                    context.ReportProgress(processed, total, $"Booked {processed}/{total}", warnings, errors);
                }
                catch (Exception ex)
                {
                    errors++;
                    _logger.LogWarning(ex, "Booking failed for draft {DraftId}", draft.Id);
                    context.ReportProgress(processed, total, $"Error: {ex.Message}", warnings, errors);
                }
            }
            context.ReportProgress(processed, total, "Booking completed.", warnings, errors);
        }
    }
}
