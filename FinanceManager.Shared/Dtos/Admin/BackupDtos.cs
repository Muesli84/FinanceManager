namespace FinanceManager.Shared.Dtos.Admin;

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
/// <returns>The result.</returns>
public sealed record BackupRestoreStatusDto(
    bool Running,
    int Processed,
    int Total,
    string? Message,
    string? Error,
    int Processed2,
    int Total2,
    string? Message2);

/// <summary>
/// Request payload used to confirm a destructive backup restore on the server.
/// </summary>
/// <param name="ConfirmationText">Text entered by the user; must match the backup file name exactly.</param>
/// <param name="ExpectedFileName">Optional client-side file-name expectation, also matched against the stored backup.</param>
/// <returns>The result.</returns>
public sealed record BackupRestoreRequestDto(string? ConfirmationText, string? ExpectedFileName = null);
