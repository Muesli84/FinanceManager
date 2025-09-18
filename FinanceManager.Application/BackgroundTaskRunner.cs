using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Application
{
    public sealed class BackgroundTaskContext
    {
        public Guid TaskId { get; }
        public Guid UserId { get; }
        public object? Payload { get; }
        public Action<int, int?, string?, int, int> ReportProgress { get; }
        public BackgroundTaskContext(Guid taskId, Guid userId, object? payload, Action<int, int?, string?, int, int> reportProgress)
        {
            TaskId = taskId;
            UserId = userId;
            Payload = payload;
            ReportProgress = reportProgress;
        }
    }

    public interface IBackgroundTaskExecutor
    {
        BackgroundTaskType Type { get; }
        Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct);
    }

    public sealed class BackgroundTaskRunner : BackgroundService
    {
        private readonly IBackgroundTaskManager _manager;
        private readonly ILogger<BackgroundTaskRunner> _logger;
        private readonly IEnumerable<IBackgroundTaskExecutor> _executors;

        public BackgroundTaskRunner(IBackgroundTaskManager manager, ILogger<BackgroundTaskRunner> logger, IEnumerable<IBackgroundTaskExecutor> executors)
        {
            _manager = manager;
            _logger = logger;
            _executors = executors;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Warte auf neue Aufgabe
                await _manager.Semaphore.WaitAsync(stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                if (_manager.TryDequeueNext(out var taskId))
                {
                    var info = _manager.Get(taskId);
                    if (info == null) continue;
                    var executor = _executors.FirstOrDefault(x => x.Type == info.Type);
                    if (executor == null)
                    {
                        _logger.LogError("No executor for task type {Type}", info.Type);
                        _manager.UpdateTaskInfo(info with { Status = BackgroundTaskStatus.Failed, ErrorDetail = "No executor found", FinishedUtc = DateTime.UtcNow });
                        _manager.Semaphore.Release();
                        continue;
                    }
                    var started = DateTime.UtcNow;
                    _manager.UpdateTaskInfo(info with { Status = BackgroundTaskStatus.Running, StartedUtc = started });
                    _logger.LogInformation("Task {TaskId} of type {Type} started by {UserId}", info.Id, info.Type, info.UserId);
                    var ctSource = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var context = new BackgroundTaskContext(
                        info.Id,
                        info.UserId,
                        null,
                        (processed, total, message, warnings, errors) =>
                        {
                            var updated = _manager.Get(info.Id) with
                            {
                                Processed = processed,
                                Total = total,
                                Message = message,
                                Warnings = warnings,
                                Errors = errors
                            };
                            _manager.UpdateTaskInfo(updated);
                        });
                    try
                    {
                        await executor.ExecuteAsync(context, ctSource.Token);
                        var finished = DateTime.UtcNow;
                        var final = _manager.Get(info.Id) with { Status = BackgroundTaskStatus.Completed, FinishedUtc = finished };
                        _manager.UpdateTaskInfo(final);
                        _logger.LogInformation("Task {TaskId} completed in {Duration}ms", info.Id, (finished - started).TotalMilliseconds);
                    }
                    catch (OperationCanceledException)
                    {
                        var finished = DateTime.UtcNow;
                        var cancelled = _manager.Get(info.Id) with { Status = BackgroundTaskStatus.Cancelled, FinishedUtc = finished };
                        _manager.UpdateTaskInfo(cancelled);
                        _logger.LogInformation("Task {TaskId} cancelled", info.Id);
                    }
                    catch (Exception ex)
                    {
                        var finished = DateTime.UtcNow;
                        var failed = _manager.Get(info.Id) with { Status = BackgroundTaskStatus.Failed, ErrorDetail = ex.Message, FinishedUtc = finished };
                        _manager.UpdateTaskInfo(failed);
                        _logger.LogError(ex, "Task {TaskId} failed: {Message}", info.Id, ex.Message);
                    }
                    finally
                    {
                        _manager.Semaphore.Release();
                    }
                }
                else
                {
                    // Keine Aufgabe, Semaphore wieder freigeben
                    _manager.Semaphore.Release();
                    await Task.Delay(500, stoppingToken);
                }
            }
        }
    }
}
