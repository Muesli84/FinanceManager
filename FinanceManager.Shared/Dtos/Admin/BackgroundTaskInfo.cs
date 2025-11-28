namespace FinanceManager.Shared.Dtos.Admin
{
    /// <summary>
    /// DTO summarizing the state and progress of a background task.
    /// </summary>
    /// <param name="Id">Unique task identifier.</param>
    /// <param name="Type">Type of the background task.</param>
    /// <param name="UserId">Owner user id for which the task runs.</param>
    /// <param name="EnqueuedUtc">UTC timestamp when the task was enqueued.</param>
    /// <param name="Status">Current status of the task.</param>
    /// <param name="Processed">Number of processed items.</param>
    /// <param name="Total">Total number of items to process.</param>
    /// <param name="Message">Optional status message.</param>
    /// <param name="Warnings">Number of warnings encountered.</param>
    /// <param name="Errors">Number of errors encountered.</param>
    /// <param name="ErrorDetail">Optional detailed error information.</param>
    /// <param name="StartedUtc">UTC timestamp when the task started.</param>
    /// <param name="FinishedUtc">UTC timestamp when the task finished.</param>
    /// <param name="Payload">Optional serialized payload associated with the task.</param>
    /// <param name="Processed2">Optional secondary processed counter.</param>
    /// <param name="Total2">Optional secondary total counter.</param>
    /// <param name="Message2">Optional secondary message.</param>
    public sealed record BackgroundTaskInfo(
        Guid Id,
        BackgroundTaskType Type,
        Guid UserId,
        DateTime EnqueuedUtc,
        BackgroundTaskStatus Status,
        int? Processed,
        int? Total,
        string? Message,
        int Warnings,
        int Errors,
        string? ErrorDetail,
        DateTime? StartedUtc,
        DateTime? FinishedUtc,
        string? Payload,
        int? Processed2,
        int? Total2,
        string? Message2
    );
}
