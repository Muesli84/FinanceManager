namespace FinanceManager.Infrastructure.Backups;

/// <summary>
/// Represents metadata for a stored backup file.
/// Instances are persisted in the database and reference a file on disk under the backups storage root.
/// </summary>
public sealed class BackupRecord
{
    /// <summary>
    /// Primary identifier of the backup record.
    /// </summary>
    /// <returns>The result.</returns>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Owner user identifier for whom the backup was created.
    /// </summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>
    /// UTC timestamp when the backup record was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// File name of the backup (original or generated name displayed to users).
    /// </summary>
    public string FileName { get; set; } = string.Empty; // original file name or generated

    /// <summary>
    /// Size of the stored file in bytes.
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Source of the backup. Typical values: <c>"Upload"</c> for user uploads or <c>"System"</c> for system-generated backups.
    /// </summary>
    public string Source { get; set; } = "Upload"; // Upload | System

    /// <summary>
    /// Relative storage path under the configured backups root where the file is stored.
    /// Consumers should combine this value with the application's backups root to access the file on disk.
    /// </summary>
    public string StoragePath { get; set; } = string.Empty; // relative path under storage root
}
