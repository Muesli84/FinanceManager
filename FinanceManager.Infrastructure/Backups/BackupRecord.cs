namespace FinanceManager.Infrastructure.Backups;

public sealed class BackupRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string FileName { get; set; } = string.Empty; // original file name or generated
    public long SizeBytes { get; set; }
    public string Source { get; set; } = "Upload"; // Upload | System
    public string StoragePath { get; set; } = string.Empty; // relative path under storage root
}
