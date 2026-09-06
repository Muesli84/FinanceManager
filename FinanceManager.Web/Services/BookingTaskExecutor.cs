using FinanceManager.Application;
using FinanceManager.Application.Statements;
using Microsoft.Extensions.Localization;
using System.Text.Json;

namespace FinanceManager.Web.Services
{
    /// <summary>
    /// Background task executor that performs mass booking of open statement drafts for a user.
    /// The executor processes open drafts in batches, reports progress to the BackgroundTaskManager
    /// and obeys provided options such as ignoring warnings or aborting on first issue.
    /// </summary>
    public sealed class BookingTaskExecutor : IBackgroundTaskExecutor
    {
        /// <summary>
        /// The background task type handled by this executor.
        /// </summary>
        public BackgroundTaskType Type => BackgroundTaskType.BookAllDrafts;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookingTaskExecutor> _logger;
        private readonly IStringLocalizer _localizer;

        /// <summary>
        /// Options parsed from the task payload that control booking behavior.
        /// </summary>
        /// <param name="IgnoreWarnings">When true warnings are ignored and booking proceeds.</param>
        /// <param name="AbortOnFirstIssue">When true the executor stops at the first error or warning condition.</param>
        /// <param name="BookEntriesIndividually">When true postings are booked entry-by-entry instead of per-draft.</param>
        /// <returns>The result.</returns>
        private sealed record Options(bool IgnoreWarnings, bool AbortOnFirstIssue, bool BookEntriesIndividually);

        /// <summary>
        /// Initializes a new instance of <see cref="BookingTaskExecutor"/>
        /// </summary>
        /// <param name="scopeFactory">Factory to create a scoped service provider for resolving scoped services during execution.</param>
        /// <param name="logger">Logger used to record errors and informational events.</param>
        /// <param name="localizer">Localizer used to produce localized progress messages.</param>
        public BookingTaskExecutor(IServiceScopeFactory scopeFactory, ILogger<BookingTaskExecutor> logger, IStringLocalizer<Pages> localizer)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _localizer = localizer;
        }

        /// <summary>
        /// Executes the mass booking background task using the provided <paramref name="context"/>.
        /// Progress is continuously reported via <see cref="BackgroundTaskContext.ReportProgress"/>.
        /// </summary>
        /// <param name="context">Background task execution context containing TaskId, UserId and Payload.</param>
        /// <param name="ct">Cancellation token that may be used to cancel execution.</param>
        /// <returns>A task that completes when processing has finished or the operation is cancelled.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the provided <paramref name="ct"/>.</exception>
        /// <exception cref="JsonException">May be thrown when the task payload contains malformed JSON while parsing options.</exception>
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
                            .Where(e => e.Status == StatementDraftEntryStatus.Accounted)
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
                        if (refreshed == null || refreshed.Status == StatementDraftStatus.Committed)
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

        /// <summary>
        /// Parses booking options from the task payload. The payload is expected to be a JSON object with boolean properties
        /// <c>IgnoreWarnings</c>, <c>AbortOnFirstIssue</c> and <c>BookEntriesIndividually</c>. When parsing fails the default
        /// options (all false) are returned.
        /// </summary>
        /// <param name="payload">Task payload provided when the background job was enqueued. May be <c>null</c> or a JSON string.</param>
        /// <returns>An <see cref="Options"/> instance with the parsed settings or defaults when parsing fails.</returns>
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
