namespace FinanceManager.Shared.Dtos.Backups;

/// <summary>
/// DTO describing a stored backup file.
/// </summary>
public sealed class BackupDto
{
    /// <summary>Unique backup identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>UTC timestamp when the backup was created.</summary>
    public DateTime CreatedUtc { get; set; }
    /// <summary>File name of the backup (zip container).</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Size of the backup file in bytes.</summary>
    public long SizeBytes { get; set; }
    /// <summary>Source indicating how the backup was created (System | Upload).</summary>
    public string Source { get; set; } = string.Empty;
}
