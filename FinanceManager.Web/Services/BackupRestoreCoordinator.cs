using System.Collections.Concurrent;
using FinanceManager.Infrastructure;
using FinanceManager.Infrastructure.Backups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using FinanceManager.Shared.Extensions;

namespace FinanceManager.Web.Services;

public sealed class BackupRestoreCoordinator : IBackupRestoreCoordinator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupRestoreCoordinator> _logger;

    private sealed class State
    {
        public bool Running;
        public int Processed;
        public int Total;
        public string? Message;
        public string? Error;
        public CancellationTokenSource Cts = new();
        public Task? CurrentTask;
    }

    private readonly ConcurrentDictionary<Guid, State> _states = new();

    public BackupRestoreCoordinator(IServiceScopeFactory scopeFactory, ILogger<BackupRestoreCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public BackupRestoreStatus? GetStatus(Guid userId)
    {
        if (_states.TryGetValue(userId, out var s))
        {
            if (!s.Running)
            {
                _states.TryRemove(userId, out _);
            }
            return new BackupRestoreStatus(s.Running, s.Processed, s.Total, s.Message, s.Error);
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

    public async Task<BackupRestoreStatus> StartAsync(Guid userId, Guid backupId, TimeSpan maxDuration, CancellationToken ct)
    {
        var state = _states.GetOrAdd(userId, _ => new State());
        if (!state.Running)
        {
            state.Running = true;
            state.Processed = 0;
            state.Total = 2; // steps: load + import
            state.Message = null;
            state.Error = null;
            state.Cts.Dispose();
            state.Cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            state.CurrentTask = Task.Run(() => RunAsync(userId, backupId, state), state.Cts.Token);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < maxDuration && state.Running)
        {
            await Task.Delay(50, ct);
        }
        var msg = state.Running ? "Still working" : (state.Error == null ? "Completed" : "Failed");
        state.Message = msg;
        return new BackupRestoreStatus(state.Running, state.Processed, state.Total, msg, state.Error);
    }

    private async Task RunAsync(Guid userId, Guid backupId, State state)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var svc = new BackupService(db, scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), scope.ServiceProvider.GetRequiredService<ILogger<BackupService>>());

            state.Message = "Reading backup";
            state.Processed = 1;
            state.Total = 2;
            if (state.Cts.Token.IsCancellationRequested) { return; }

            var ok = await svc.ApplyAsync(userId, backupId, (step, count) => { state.Total = count + 1; state.Processed = 1 + step; }, state.Cts.Token);
            if (!ok)
            {
                state.Error = "Apply failed";
                return;
            }
            state.Message = "Imported";
            state.Processed = 2;
        }
        catch (OperationCanceledException)
        {
            state.Error = "Canceled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup restore failed for {User}", userId);
            state.Error = ex.ToMessageWithInner();
        }
        finally
        {
            state.Running = false;
        }
    }
}
