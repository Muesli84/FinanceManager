using System.Collections.Concurrent;

namespace FinanceManager.Application
{
    /// <summary>
    /// Manager interface for enqueueing and querying background tasks.
    /// Implementations coordinate task queueing, cancellation and status updates.
    /// </summary>
    public interface IBackgroundTaskManager
    {
        /// <summary>
        /// Enqueues a background task.
        /// </summary>
        /// <param name="type">Type of the background task.</param>
        /// <param name="userId">User id the task belongs to.</param>
        /// <param name="payload">Optional payload object provided to the task.</param>
        /// <param name="allowDuplicate">When true allows enqueuing a duplicate task.</param>
        /// <returns>Information about the enqueued task.</returns>
        BackgroundTaskInfo Enqueue(BackgroundTaskType type, Guid userId, object? payload = null, bool allowDuplicate = false);

        /// <summary>
        /// Returns all known tasks (queued, running, completed) managed by the instance.
        /// </summary>
        /// <returns>The result.</returns>
        IReadOnlyList<BackgroundTaskInfo> GetAll();

        /// <summary>
        /// Gets the task info for the specified id or null when not found.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>The result.</returns>
        BackgroundTaskInfo? Get(Guid id);

        /// <summary>
        /// Tries to cancel the specified task. Returns true when cancellation requested.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        bool TryCancel(Guid id);

        /// <summary>
        /// Attempts to remove a queued task prior to execution.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        bool TryRemoveQueued(Guid id);

        /// <summary>
        /// Attempts to dequeue the next queued task id for processing.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        bool TryDequeueNext(out Guid id);

        /// <summary>
        /// Updates stored information about a background task (status/progress/message).
        /// </summary>
        /// <param name="info">The info.</param>
        void UpdateTaskInfo(BackgroundTaskInfo info);

        /// <summary>
        /// Semaphore to coordinate single consumer processing.
        /// </summary>
        SemaphoreSlim Semaphore { get; }
    }

    /// <summary>
    /// Default in-memory implementation of <see cref="IBackgroundTaskManager"/> used for queuing and managing background tasks.
    /// </summary>
    public sealed class BackgroundTaskManager : IBackgroundTaskManager
    {
        private readonly ConcurrentQueue<Guid> _queue = new();
        private readonly ConcurrentDictionary<Guid, BackgroundTaskInfo> _tasks = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly object _lock = new();

        /// <summary>
        /// Enqueues a background task and returns its info record.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="userId">The user id.</param>
        /// <param name="payload">The payload.</param>
        /// <param name="allowDuplicate">The allow duplicate.</param>
        /// <returns>The result.</returns>
        public BackgroundTaskInfo Enqueue(BackgroundTaskType type, Guid userId, object? payload = null, bool allowDuplicate = false)
        {
            lock (_lock)
            {
                // Idempotenz: Prüfe, ob Task gleichen Typs für denselben Benutzer bereits läuft oder queued ist
                foreach (var info in _tasks.Values)
                {
                    if (info.Type == type && info.UserId == userId && (info.Status == BackgroundTaskStatus.Running || info.Status == BackgroundTaskStatus.Queued))
                    {
                        if (!allowDuplicate)
                            return info;
                    }
                }
                var id = Guid.NewGuid();
                var now = DateTime.UtcNow;
                string? payloadJson = null;
                if (payload != null)
                {
                    try { payloadJson = System.Text.Json.JsonSerializer.Serialize(payload); } catch { }
                }
                var taskInfo = new BackgroundTaskInfo(
                    id,
                    type,
                    userId,
                    now,
                    BackgroundTaskStatus.Queued,
                    null,
                    null,
                    null,
                    0,
                    0,
                    null,
                    null,
                    null,
                    payloadJson,
                    null,
                    null,
                    null
                );
                _tasks[id] = taskInfo;
                _queue.Enqueue(id);
                return taskInfo;
            }
        }

        /// <summary>
        /// Returns all known tasks.
        /// </summary>
        /// <returns>The result.</returns>
        public IReadOnlyList<BackgroundTaskInfo> GetAll()
        {
            return new List<BackgroundTaskInfo>(_tasks.Values);
        }

        /// <summary>
        /// Gets a specific task by id or null when not found.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>The result.</returns>
        public BackgroundTaskInfo? Get(Guid id)
        {
            _tasks.TryGetValue(id, out var info);
            return info;
        }

        /// <summary>
        /// Attempts to cancel a task by id.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool TryCancel(Guid id)
        {
            lock (_lock)
            {
                if (_tasks.TryGetValue(id, out var info) && info.Status == BackgroundTaskStatus.Running)
                {
                    // Status auf Cancelled setzen (Executor muss ct beachten)
                    var cancelled = info with { Status = BackgroundTaskStatus.Cancelled, FinishedUtc = DateTime.UtcNow };
                    _tasks[id] = cancelled;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Attempts to remove a queued task prior to start.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool TryRemoveQueued(Guid id)
        {
            lock (_lock)
            {
                if (_tasks.TryGetValue(id, out var info) && info.Status == BackgroundTaskStatus.Queued)
                {
                    _tasks.TryRemove(id, out _);
                    // Queue bereinigen
                    var newQueue = new ConcurrentQueue<Guid>();
                    foreach (var qid in _queue)
                    {
                        if (qid != id) newQueue.Enqueue(qid);
                    }
                    while (_queue.TryDequeue(out _)) { }
                    foreach (var qid in newQueue) _queue.Enqueue(qid);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Attempts to dequeue the next queued task id for processing.
        /// </summary>
        /// <param name="id">Identifier of the entity.</param>
        /// <returns>Whether the operation succeeded.</returns>
        public bool TryDequeueNext(out Guid id)
        {
            return _queue.TryDequeue(out id);
        }

        /// <summary>
        /// Updates stored information about a background task.
        /// </summary>
        /// <param name="info">The info.</param>
        public void UpdateTaskInfo(BackgroundTaskInfo info)
        {
            _tasks[info.Id] = info;
        }

        /// <summary>
        /// Semaphore used to coordinate background task processing.
        /// </summary>
        public SemaphoreSlim Semaphore => _semaphore;
    }
}
