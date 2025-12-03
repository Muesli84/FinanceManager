using System.Collections.Concurrent;

namespace FinanceManager.Application
{
    public interface IBackgroundTaskManager
    {
        BackgroundTaskInfo Enqueue(BackgroundTaskType type, Guid userId, object? payload = null, bool allowDuplicate = false);
        IReadOnlyList<BackgroundTaskInfo> GetAll();
        BackgroundTaskInfo? Get(Guid id);
        bool TryCancel(Guid id);
        bool TryRemoveQueued(Guid id);
        // Runner operations
        bool TryDequeueNext(out Guid id);
        void UpdateTaskInfo(BackgroundTaskInfo info);
        SemaphoreSlim Semaphore { get; }
    }

    public sealed class BackgroundTaskManager : IBackgroundTaskManager
    {
        private readonly ConcurrentQueue<Guid> _queue = new();
        private readonly ConcurrentDictionary<Guid, BackgroundTaskInfo> _tasks = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly object _lock = new();

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

        public IReadOnlyList<BackgroundTaskInfo> GetAll()
        {
            return new List<BackgroundTaskInfo>(_tasks.Values);
        }

        public BackgroundTaskInfo? Get(Guid id)
        {
            _tasks.TryGetValue(id, out var info);
            return info;
        }

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

        // Für Runner: Hole nächste TaskId aus Queue
        public bool TryDequeueNext(out Guid id)
        {
            return _queue.TryDequeue(out id);
        }

        // Für Runner: Setze TaskInfo (Status/Fortschritt)
        public void UpdateTaskInfo(BackgroundTaskInfo info)
        {
            _tasks[info.Id] = info;
        }

        // Für Runner: Semaphore für exklusiven Task
        public SemaphoreSlim Semaphore => _semaphore;
    }
}
