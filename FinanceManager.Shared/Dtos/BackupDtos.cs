namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO indicating the current status of a backup restore operation.
/// </summary>
/// <param name="Running">True when the restore task is currently running.</param>
/// <param name="Processed">Number of processed records.</param>
/// <param name="Total">Total number of records to process.</param>
/// <param name="Message">Optional status message.</param>
/// <param name="Error">Optional error message.</param>
/// <param name="Processed2">Optional secondary processed counter.</param>
/// <param name="Total2">Optional secondary total counter.</param>
/// <param name="Message2">Optional secondary message.</param>
public sealed record BackupRestoreStatusDto(
    bool Running,
    int Processed,
    int Total,
    string? Message,
    string? Error,
    int Processed2,
    int Total2,
    string? Message2);
