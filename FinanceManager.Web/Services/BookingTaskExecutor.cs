using System;
using System.Linq;
using System.Text.Json;
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
    public sealed class BookingTaskExecutor : IBackgroundTaskExecutor
    {
        public BackgroundTaskType Type => BackgroundTaskType.BookAllDrafts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingTaskExecutor> _logger;
        private readonly IStringLocalizer<BookingTaskExecutor> _localizer;

        private sealed record Options(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);

        public BookingTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<BookingTaskExecutor> logger, IStringLocalizer<BookingTaskExecutor> localizer)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _localizer = localizer;
        }

        public async Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct)
        {
            var opts = ParseOptions(context.Payload);
            using var scope = _scopeFactory.CreateScope();
            var draftService = scope.ServiceProvider.GetRequiredService<IStatementDraftService>();

            int totalDrafts = await draftService.GetOpenDraftsCountAsync(context.UserId, ct);
            int processedDrafts = 0;
            int failedDrafts = 0;
            int warnings = 0;
            int errors = 0;
            context.ReportProgress(processedDrafts, totalDrafts, _localizer["BK_Start"], warnings, errors);

            const int pageSize = 5;
            int skip = 0;
            while (!ct.IsCancellationRequested)
            {
                var batch = await draftService.GetOpenDraftsAsync(context.UserId, skip, pageSize, ct);
                if (batch.Count == 0) { break; }

                foreach (var draft in batch)
                {
                    ct.ThrowIfCancellationRequested();

                    if (opts.BookEntriesIndividually)
                    {
                        var orderedEntries = draft.Entries
                            .Where(e => e.Status == FinanceManager.Domain.Statements.StatementDraftEntryStatus.Accounted)
                            .OrderBy(e => e.BookingDate)
                            .ThenBy(e => e.Id)
                            .ToList();

                        bool draftFailed = false;
                        foreach (var entry in orderedEntries)
                        {
                            ct.ThrowIfCancellationRequested();
                            var result = await draftService.BookAsync(draft.DraftId, entry.Id, context.UserId, opts.IgnoreWarnings, ct);
                            var entryErrors = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                            var entryWarnings = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase)).ToList();

                            if (entryErrors.Count > 0)
                            {
                                errors += entryErrors.Count;
                                warnings += entryWarnings.Count;
                                draftFailed = true;
                                if (opts.AbortOnFirstIssue)
                                {
                                    context.ReportProgress(processedDrafts, totalDrafts, _localizer["BK_ErrorEntry", entry.Id], warnings, errors);
                                    return;
                                }
                            }
                            else if (!result.Success && result.HasWarnings)
                            {
                                warnings += entryWarnings.Count;
                                draftFailed = true;
                                if (opts.AbortOnFirstIssue)
                                {
                                    context.ReportProgress(processedDrafts, totalDrafts, _localizer["BK_WarnEntry", entry.Id], warnings, errors);
                                    return;
                                }
                            }
                            else
                            {
                                warnings += entryWarnings.Count;
                            }
                        }

                        var refreshed = await draftService.GetDraftAsync(draft.DraftId, context.UserId, ct);
                        if (refreshed == null || refreshed.Status == FinanceManager.Domain.StatementDraftStatus.Committed)
                        {
                            processedDrafts++;
                        }
                        else if (draftFailed)
                        {
                            failedDrafts++;
                            skip++;
                        }
                        else
                        {
                            skip++;
                            processedDrafts++;
                        }
                    }
                    else
                    {
                        var result = await draftService.BookAsync(draft.DraftId, null, context.UserId, opts.IgnoreWarnings, ct);
                        var draftErrors = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                        var draftWarnings = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase)).ToList();

                        if (draftErrors.Count > 0)
                        {
                            errors += draftErrors.Count;
                            failedDrafts++;
                            if (opts.AbortOnFirstIssue)
                            {
                                context.ReportProgress(processedDrafts, totalDrafts, _localizer["BK_ErrorDraft", draft.Description ?? draft.DraftId.ToString()], warnings, errors);
                                return;
                            }
                        }
                        else if (!result.Success && result.HasWarnings)
                        {
                            warnings += draftWarnings.Count;
                            failedDrafts++;
                            if (opts.AbortOnFirstIssue)
                            {
                                context.ReportProgress(processedDrafts, totalDrafts, _localizer["BK_WarnDraft", draft.Description ?? draft.DraftId.ToString()], warnings, errors);
                                return;
                            }
                        }
                        else
                        {
                            warnings += draftWarnings.Count;
                            processedDrafts++;
                        }
                        skip++;
                    }

                    context.ReportProgress(processedDrafts + failedDrafts, totalDrafts, _localizer["BK_Progress", processedDrafts, totalDrafts], warnings, errors);
                }
            }

            context.ReportProgress(processedDrafts + failedDrafts, totalDrafts, _localizer["BK_Completed"], warnings, errors);
        }

        private static Options ParseOptions(object? payload)
        {
            if (payload is string s && !string.IsNullOrWhiteSpace(s))
            {
                try
                {
                    var doc = JsonDocument.Parse(s);
                    bool GetBool(string name) => doc.RootElement.TryGetProperty(name, out var el) && el.GetBoolean();
                    return new Options(
                        IgnoreWarnings: GetBool("IgnoreWarnings"),
                        AbortOnFirstIssue: GetBool("AbortOnFirstIssue"),
                        BookEntriesIndividually: GetBool("BookEntriesIndividually")
                    );
                }
                catch { }
            }
            return new Options(false, false, false);
        }
    }
}
