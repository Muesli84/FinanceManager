namespace FinanceManager.Shared.Dtos.Admin
{
    /// <summary>
    /// Represents the current status of a background task.
    /// </summary>
    public enum BackgroundTaskStatus
    {
        /// <summary>Task is queued and waiting to run.</summary>
        Queued,
        /// <summary>Task is currently running.</summary>
        Running,
        /// <summary>Task finished successfully.</summary>
        Completed,
        /// <summary>Task failed with an error.</summary>
        Failed,
        /// <summary>Task was cancelled.</summary>
        Cancelled
    }
}
