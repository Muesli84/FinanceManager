using FinanceManager.Application.Statements;
using FinanceManager.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Collections.Concurrent;

namespace FinanceManager.Web.Services;

public sealed class ClassificationCoordinator : IClassificationCoordinator
{
    private readonly IServiceProvider serviceProvider;    
    private readonly ILogger<ClassificationCoordinator> _logger;

    private sealed class State
    {
        public bool Running;
        public int Processed;
        public int Total;
        public CancellationTokenSource Cts = new();
        public Task? CurrentTask;
        public string? Message;
    }

    private readonly ConcurrentDictionary<Guid, State> _states = new();
    
    public ClassificationCoordinator(IServiceProvider serviceProvider, ILogger<ClassificationCoordinator> logger)
    {
        this.serviceProvider = serviceProvider;
        _logger = logger;
    }

    public ClassificationStatus? GetStatus(Guid userId)
    {
        if (_states.TryGetValue(userId, out var s))
        {
            return new ClassificationStatus(s.Running, s.Processed, s.Total, s.Message);
        }
        return null;
    }

    public async Task<ClassificationStatus> ProcessAsync(Guid userId, TimeSpan maxDuration, CancellationToken ct)
    {
        var state = _states.GetOrAdd(userId, _ => new State());
        if (!state.Running)
        {
            state.Running = true;
            state.Processed = 0;
            state.Total = 0;
            state.Message = null;
            state.Cts.Dispose();
            state.Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            state.CurrentTask = Task.Run(() => RunLoopAsync(userId, state), state.Cts.Token);
        }

        // Allow worker to progress for given max duration
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < maxDuration && state.Running)
        {
            await Task.Delay(50, ct);
        }

        var msg = state.Running ? "Classifying statement drafts" : "Completed";
        state.Message = msg;
        return new ClassificationStatus(state.Running, state.Processed, state.Total, msg);
    }

    private async Task RunLoopAsync(Guid userId, State state)
    {
        try
        {
            using (var scope = serviceProvider.CreateScope())
            {
                var drafts =  scope.ServiceProvider.GetRequiredService<IStatementDraftService>();
                // page through drafts to avoid loading everything at once
                const int pageSize = 2;
                int skip = 0;
                state.Total = await drafts.GetOpenDraftsCountAsync(userId, state.Cts.Token);
                while (!state.Cts.IsCancellationRequested)
                {
                    var batch = await drafts.GetOpenDraftsAsync(userId, skip, pageSize, state.Cts.Token);
                    if (batch.Count == 0) { break; }

                    foreach (var draft in batch)
                    {
                        if (state.Cts.IsCancellationRequested) { return; }
                        await drafts.ClassifyAsync(draft.DraftId, null, userId, state.Cts.Token);
                        state.Processed++;
                    }
                    skip += batch.Count;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignored – cooperative cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Classification loop failed for {User}", userId);
            state.Message = ex.Message;
        }
        finally
        {
            state.Running = false;
        }
    }
}
