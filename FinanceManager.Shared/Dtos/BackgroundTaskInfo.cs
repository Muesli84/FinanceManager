using System;

namespace FinanceManager.Shared.Dtos
{
    public enum BackgroundTaskType
    {
        ClassifyAllDrafts,
        BookAllDrafts,
        BackupRestore,
        SecurityPricesBackfill, // NEW
        RebuildAggregates // NEW: rebuild posting aggregates for current user
    }

    public enum BackgroundTaskStatus
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }

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