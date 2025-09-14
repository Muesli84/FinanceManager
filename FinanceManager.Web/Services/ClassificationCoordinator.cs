using System.Collections.Concurrent;
using FinanceManager.Application.Statements;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Services;

public sealed class ClassificationCoordinator : IClassificationCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
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

    public ClassificationCoordinator(IServiceScopeFactory scopeFactory, ILogger<ClassificationCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
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
            const int pageSize = 2;
            int skip = 0;
            while (!state.Cts.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var drafts = scope.ServiceProvider.GetRequiredService<IStatementDraftService>();

                var batch = await drafts.GetOpenDraftsAsync(userId, skip, pageSize, state.Cts.Token);
                if (skip == 0) { state.Total = await drafts.GetOpenDraftsCountAsync(userId, state.Cts.Token); }
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
        catch (OperationCanceledException)
        {
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
