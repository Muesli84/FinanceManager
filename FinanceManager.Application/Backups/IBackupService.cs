namespace FinanceManager.Application.Backups;

/// <summary>
/// Enumerates the possible outcomes of a destructive backup restore attempt.
/// </summary>
public enum BackupApplyStatus
{
    /// <summary>Restore completed successfully.</summary>
    Succeeded,
    /// <summary>The backup metadata or file could not be found.</summary>
    NotFound,
    /// <summary>The stored backup failed security or schema validation.</summary>
    InvalidBackup,
    /// <summary>The request did not include the required file-name confirmation.</summary>
    ConfirmationRequired,
    /// <summary>The importer failed after validation succeeded.</summary>
    ImportFailed
}

/// <summary>
/// Result returned by the backup service for a restore attempt.
/// </summary>
/// <param name="Status">Machine-readable restore status.</param>
/// <param name="Message">Optional user-facing or diagnostic message.</param>
/// <returns>The result.</returns>
public sealed record BackupApplyResult(BackupApplyStatus Status, string? Message = null)
{
    /// <summary>Creates a successful restore result.</summary>
    /// <returns>The result.</returns>
    public static BackupApplyResult Succeeded() => new(BackupApplyStatus.Succeeded);
    /// <summary>Creates a not-found restore result.</summary>
    /// <param name="message">The message.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static BackupApplyResult NotFound(string? message = null) => new(BackupApplyStatus.NotFound, message);
    /// <summary>Creates an invalid-backup restore result.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static BackupApplyResult InvalidBackup(string message) => new(BackupApplyStatus.InvalidBackup, message);
    /// <summary>Creates a confirmation-required restore result.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static BackupApplyResult ConfirmationRequired(string message) => new(BackupApplyStatus.ConfirmationRequired, message);
    /// <summary>Creates an import-failed restore result.</summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static BackupApplyResult ImportFailed(string message) => new(BackupApplyStatus.ImportFailed, message);
}

/// <summary>
/// Exception used for expected, user-visible backup container and schema validation failures.
/// </summary>
public sealed class BackupValidationException : Exception
{
    /// <summary>
    /// Initializes a new validation exception with a machine-readable code and message.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="message">The message.</param>
    public BackupValidationException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>
    /// Initializes a new validation exception with a machine-readable code, message and inner exception.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The inner exception.</param>
    public BackupValidationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>
    /// Machine-readable validation error code.
    /// </summary>
    public string Code { get; }
}

/// <summary>
/// Service interface for creating, listing, uploading and applying user backups.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new backup snapshot for the specified user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BackupDto> CreateAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Lists available backups for the specified user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<BackupDto>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Deletes a backup for the user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>
    /// Opens a read stream for downloading a backup or null when not found.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<Stream?> OpenDownloadAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>
    /// Gets a single backup metadata entry for the user.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BackupDto?> GetAsync(Guid userId, Guid id, CancellationToken ct);

    /// <summary>
    /// Applies a backup synchronously and reports progress through a callback.
    /// </summary>
    /// <param name="userId">Owner user identifier.</param>
    /// <param name="id">Backup identifier.</param>
    /// <param name="confirmationText">Confirmation text that must match the backup file name.</param>
    /// <param name="confirmationAlreadyValidated">True when an enqueue endpoint already validated the confirmation before creating a background task.</param>
    /// <param name="progressCallback">Callback receiving message and progress values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BackupApplyResult> ApplyAsync(
        Guid userId,
        Guid id,
        string? confirmationText,
        bool confirmationAlreadyValidated,
        Action<string, int, int, int, int> progressCallback,
        CancellationToken ct);

    /// <summary>
    /// Uploads a backup file and returns created metadata DTO.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="stream">The stream.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<BackupDto> UploadAsync(Guid userId, Stream stream, string fileName, CancellationToken ct);
}


