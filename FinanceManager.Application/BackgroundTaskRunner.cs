using FinanceManager.Shared.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Application
{
    /// <summary>
    /// Context information provided to a background task executor when executing a task.
    /// </summary>
    public sealed class BackgroundTaskContext
    {
        /// <summary>
        /// Unique identifier of the background task.
        /// </summary>
        public Guid TaskId { get; }

        /// <summary>
        /// Owner user identifier for whom the task runs.
        /// </summary>
        public Guid UserId { get; }

        /// <summary>
        /// Optional payload object passed to the task.
        /// </summary>
        public object? Payload { get; }

        /// <summary>
        /// Delegate used by the task to report progress updates.
        /// Signature: (processed, total?, message?, processed2, total2)
        /// </summary>
        public Action<int, int?, string?, int, int> ReportProgress { get; }

        /// <summary>
        /// Creates a new BackgroundTaskContext instance.
        /// </summary>
        /// <param name="taskId">The task id.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="payload">The payload.</param>
        /// <param name="reportProgress">The report progress.</param>
        public BackgroundTaskContext(Guid taskId, Guid userId, object? payload, Action<int, int?, string?, int, int> reportProgress)
        {
            TaskId = taskId;
            UserId = userId;
            Payload = payload;
            ReportProgress = reportProgress;
        }
    }

    /// <summary>
    /// Executor interface that background task implementations must implement.
    /// </summary>
    public interface IBackgroundTaskExecutor
    {
        /// <summary>
        /// The background task type handled by this executor.
        /// </summary>
        BackgroundTaskType Type { get; }

        /// <summary>
        /// Executes a background task with the provided context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="ct">Cancellation token.</param>
        Task ExecuteAsync(BackgroundTaskContext context, CancellationToken ct);
    }

    /// <summary>
    /// Background service that runs and dispatches background tasks using registered executors.
    /// </summary>
    public sealed class BackgroundTaskRunner : BackgroundService
    {
        private readonly IBackgroundTaskManager _manager;
        private readonly ILogger<BackgroundTaskRunner> _logger;
        private readonly IEnumerable<IBackgroundTaskExecutor> _executors;

        /// <summary>
        /// Creates a new BackgroundTaskRunner.
        /// </summary>
        /// <param name="manager">The manager.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="executors">The executors.</param>
        public BackgroundTaskRunner(IBackgroundTaskManager manager, ILogger<BackgroundTaskRunner> logger, IEnumerable<IBackgroundTaskExecutor> executors)
        {
            _manager = manager;
            _logger = logger;
            _executors = executors;
        }

        /// <summary>
        /// Main execution loop for background task processing.
        /// </summary>
        /// <param name="stoppingToken">Cancellation token used to stop the background service.</param>
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
                    if (info == null) { _manager.Semaphore.Release(); continue; }
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
                        info.Payload, // pass raw JSON payload
                        (processed, total, message, warnings, errors) =>
                        {
                            var previous = _manager.Get(info.Id);
                            if (previous is null)
                            {
                                return;
                            }
                            var updated = previous with
                            {
                                Processed = processed,
                                Total = total,
                                Message = message ?? previous.Message,
                                Warnings = warnings,
                                Errors = errors
                            };
                            _manager.UpdateTaskInfo(updated);
                        });
                    try
                    {
                        await executor.ExecuteAsync(context, ctSource.Token);
                        var finished = DateTime.UtcNow;
                        if (_manager.Get(info.Id) is { } current)
                        {
                            _manager.UpdateTaskInfo(current with { Status = BackgroundTaskStatus.Completed, FinishedUtc = finished });
                        }
                        _logger.LogInformation("Task {TaskId} completed in {Duration}ms", info.Id, (finished - started).TotalMilliseconds);
                    }
                    catch (OperationCanceledException)
                    {
                        var finished = DateTime.UtcNow;
                        if (_manager.Get(info.Id) is { } current)
                        {
                            _manager.UpdateTaskInfo(current with { Status = BackgroundTaskStatus.Cancelled, FinishedUtc = finished });
                        }
                        _logger.LogInformation("Task {TaskId} cancelled", info.Id);
                    }
                    catch (Exception ex)
                    {
                        var finished = DateTime.UtcNow;
                        if (_manager.Get(info.Id) is { } current)
                        {
                            _manager.UpdateTaskInfo(current with { Status = BackgroundTaskStatus.Failed, ErrorDetail = ex.ToMessageWithInner(), FinishedUtc = finished });
                        }
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
