namespace FinanceManager.Shared.Dtos.Admin
{
    /// <summary>
    /// Defines the type of a background task.
    /// </summary>
    public enum BackgroundTaskType
    {
        /// <summary>Classify all open statement drafts.</summary>
        ClassifyAllDrafts,
        /// <summary>Book all open statement drafts.</summary>
        BookAllDrafts,
        /// <summary>Restore a backup for the current user.</summary>
        BackupRestore,
        /// <summary>Backfill historical security prices.</summary>
        SecurityPricesBackfill,
        /// <summary>Rebuild posting aggregates for the current user.</summary>
        RebuildAggregates
    }
}
