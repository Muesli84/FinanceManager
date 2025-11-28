namespace FinanceManager.Application.Backups;

public interface IBackupService
{
    Task<BackupDto> CreateAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<BackupDto>> ListAsync(Guid userId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct);
    Task<Stream?> OpenDownloadAsync(Guid userId, Guid id, CancellationToken ct);
    Task<bool> ApplyAsync(Guid userId, Guid id, Action<string, int, int, int, int> progressCallback, CancellationToken ct);
    Task<BackupDto> UploadAsync(Guid userId, Stream stream, string fileName, CancellationToken ct);
}


