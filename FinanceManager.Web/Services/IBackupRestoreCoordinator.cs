namespace FinanceManager.Web.Services;

public interface IBackupRestoreCoordinator
{
    BackupRestoreStatus? GetStatus(Guid userId);
    void Cancel(Guid userId);
    Task<BackupRestoreStatus> StartAsync(Guid userId, Guid backupId, TimeSpan maxDuration, CancellationToken ct);
}

public sealed record BackupRestoreStatus(
    bool Running,
    int Processed,
    int Total,
    string? Message,
    string? Error,
    int Processed2 = 0,
    int Total2 = 0,
    string? Message2 = null);
