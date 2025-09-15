using System.Collections.Concurrent;
using FinanceManager.Application.Statements;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services;

public sealed class BookingCoordinator : IBookingCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingCoordinator> _logger;

    private sealed class State
    {
        public bool Running;
        public int Processed;
        public int Failed;
        public int Total;
        public int Warnings;
        public int Errors;
        public CancellationTokenSource Cts = new();
        public Task? CurrentTask;
        public string? Message;
        public bool IgnoreWarnings;
        public bool AbortOnFirstIssue;
        public bool BookEntriesIndividually;
        public List<BookingIssue> Issues = new();
    }

    private readonly ConcurrentDictionary<Guid, State> _states = new();

    public BookingCoordinator(IServiceScopeFactory scopeFactory, ILogger<BookingCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public BookingStatus? GetStatus(Guid userId)
    {
        if (_states.TryGetValue(userId, out var s))
        {
            if (!s.Running)
                _states.TryRemove(userId, out var _);
            return new BookingStatus(s.Running, s.Processed, s.Failed, s.Total, s.Message, s.Warnings, s.Errors, s.Issues.ToList());
        }
        return null;
    }

    public void Cancel(Guid userId)
    {
        if (_states.TryGetValue(userId, out var s))
        {
            try { s.Cts.Cancel(); } catch { }
        }
    }

    public async Task<BookingStatus> ProcessAsync(Guid userId, bool ignoreWarnings, bool abortOnFirstIssue, bool bookEntriesIndividually, TimeSpan maxDuration, CancellationToken ct)
    {
        var state = _states.GetOrAdd(userId, _ => new State());
        if (!state.Running)
        {
            state.Running = true;
            state.Processed = 0;
            state.Failed = 0;
            state.Total = 0;
            state.Warnings = 0;
            state.Errors = 0;
            state.Message = null;
            state.IgnoreWarnings = ignoreWarnings;
            state.AbortOnFirstIssue = abortOnFirstIssue;
            state.BookEntriesIndividually = bookEntriesIndividually;
            state.Issues.Clear();
            state.Cts.Dispose();
            state.Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            state.CurrentTask = Task.Run(() => RunLoopAsync(userId, state), state.Cts.Token);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < maxDuration && state.Running)
        {
            await Task.Delay(50, ct);
        }

        var msg = state.Running ? "Still working" : "Completed";
        state.Message = msg;
        return new BookingStatus(state.Running, state.Processed, state.Failed, state.Total, msg, state.Warnings, state.Errors, state.Issues.ToList());
    }

    private async Task RunLoopAsync(Guid userId, State state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var drafts = scope.ServiceProvider.GetRequiredService<IStatementDraftService>();
            state.Total = await drafts.GetOpenDraftsCountAsync(userId, state.Cts.Token);
            const int pageSize = 2;
            int skip = 0;
            while (!state.Cts.IsCancellationRequested)
            {
                var batch = await drafts.GetOpenDraftsAsync(userId, skip, pageSize, state.Cts.Token);
                if (batch.Count == 0) { break; }

                foreach (var draft in batch)
                {
                    if (state.Cts.IsCancellationRequested) { return; }

                    if (state.BookEntriesIndividually)
                    {
                        // Alle offenen/angekündigten Einträge einzeln buchen
                        var openEntries = draft.Entries
                            .Where(e => e.Status is Domain.Statements.StatementDraftEntryStatus.Accounted)
                            .OrderBy(e => e.BookingDate)
                            .ThenBy(e => e.Id)
                            .ToList();

                        foreach (var entry in openEntries)
                        {
                            if (state.Cts.IsCancellationRequested) { return; }

                            var result = await drafts.BookAsync(draft.DraftId, entry.Id, userId, state.IgnoreWarnings, state.Cts.Token);

                            var errorMsgs = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                            var warnMsgs = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase)).ToList();

                            var errorCount = errorMsgs.Count;
                            var warnCount = warnMsgs.Count;

                            if (errorCount > 0)
                            {
                                state.Errors += errorCount;
                                foreach (var em in errorMsgs)
                                {
                                    state.Issues.Add(new BookingIssue(draft.DraftId, em.EntryId, em.Code, em.Message));
                                }
                                state.Message = $"Error on entry {entry.Id} of draft {draft.Description}";
                                if (state.AbortOnFirstIssue) { return; }
                            }
                            else if (!result.Success && result.HasWarnings)
                            {
                                state.Warnings += warnCount;
                                foreach (var wm in warnMsgs)
                                {
                                    state.Issues.Add(new BookingIssue(draft.DraftId, wm.EntryId, wm.Code, wm.Message));
                                }
                                state.Message = $"Warning on entry {entry.Id} of draft {draft.Description}";
                                if (state.AbortOnFirstIssue) { return; }
                            }
                            else
                            {
                                // Erfolg (ggf. mit ignorierten Warnungen)
                                state.Warnings += warnCount;
                                foreach (var wm in warnMsgs)
                                {
                                    state.Issues.Add(new BookingIssue(draft.DraftId, wm.EntryId, wm.Code, wm.Message));
                                }
                            }
                        }

                        var checkDraft = await drafts.GetDraftAsync(draft.DraftId, userId, state.Cts.Token);
                        if (checkDraft is null)
                            state.Processed++;
                        else if (checkDraft.Status == Domain.StatementDraftStatus.Committed)
                            state.Processed++;
                        else
                            state.Failed++;
                    }
                    else
                    {
                        // Draft als Ganzes buchen
                        var result = await drafts.BookAsync(draft.DraftId, null, userId, state.IgnoreWarnings, state.Cts.Token);

                        var errorMsgs = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Error", StringComparison.OrdinalIgnoreCase)).ToList();
                        var warnMsgs = result.Validation.Messages.Where(m => string.Equals(m.Severity, "Warning", StringComparison.OrdinalIgnoreCase)).ToList();

                        var errorCount = errorMsgs.Count;
                        var warnCount = warnMsgs.Count;

                        if (errorCount > 0)
                        {
                            state.Errors += errorCount;
                            state.Failed++;
                            foreach (var em in errorMsgs)
                            {
                                state.Issues.Add(new BookingIssue(draft.DraftId, em.EntryId, em.Code, em.Message));
                            }
                            state.Message = $"Error on draft {draft.Description}";
                            if (state.AbortOnFirstIssue) { return; }
                        }
                        else if (!result.Success && result.HasWarnings)
                        {
                            state.Warnings += warnCount;
                            state.Failed++;
                            foreach (var wm in warnMsgs)
                            {
                                state.Issues.Add(new BookingIssue(draft.DraftId, wm.EntryId, wm.Code, wm.Message));
                            }
                            state.Message = $"Warning on draft {draft.Description}";
                            if (state.AbortOnFirstIssue) { return; }
                        }
                        else
                        {
                            // Erfolg (ggf. mit ignorierten Warnungen)
                            state.Warnings += warnCount;
                            foreach (var wm in warnMsgs)
                            {
                                state.Issues.Add(new BookingIssue(draft.DraftId, wm.EntryId, wm.Code, wm.Message));
                            }
                            state.Processed++;
                        }
                    }
                }

                skip++;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Booking loop failed for {User}", userId);
            state.Message = ex.Message;
        }
        finally
        {
            state.Running = false;
        }
    }
}