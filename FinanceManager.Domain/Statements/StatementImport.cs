namespace FinanceManager.Domain.Statements;

/// <summary>
/// Represents a single import operation of a bank statement file for a specific account.
/// Contains metadata about the import (format, original filename, timestamps and totals).
/// </summary>
public sealed class StatementImport : Entity, IAggregateRoot
{
    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private StatementImport() { }

    /// <summary>
    /// Creates a new <see cref="StatementImport"/> for the specified account and file.
    /// </summary>
    /// <param name="accountId">Identifier of the account the statement belongs to. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="format">Detected import format.</param>
    /// <param name="originalFileName">Original uploaded file name. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId"/> is empty or <paramref name="originalFileName"/> is null/whitespace (see guard helpers).</exception>
    public StatementImport(Guid accountId, ImportFormat format, string originalFileName)
    {
        AccountId = Guards.NotEmpty(accountId, nameof(accountId));
        Format = format;
        OriginalFileName = Guards.NotNullOrWhiteSpace(originalFileName, nameof(originalFileName));
        ImportedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Identifier of the account this import belongs to.
    /// </summary>
    /// <value>The account GUID.</value>
    public Guid AccountId { get; private set; }

    /// <summary>
    /// Detected import format (CSV, MT940, etc.).
    /// </summary>
    /// <value>The import format.</value>
    public ImportFormat Format { get; private set; }

    /// <summary>
    /// Timestamp (UTC) when the file was imported.
    /// </summary>
    /// <value>The import time in UTC.</value>
    public DateTime ImportedAtUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Original uploaded file name as provided by the user.
    /// </summary>
    /// <value>Original file name string.</value>
    public string OriginalFileName { get; private set; } = null!;

    /// <summary>
    /// Number of entries parsed from the imported statement file.
    /// </summary>
    /// <value>Total number of parsed entries.</value>
    public int TotalEntries { get; private set; }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="StatementImport"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the statement import entity.</param>
    /// <param name="AccountId">Identifier of the account this import belongs to.</param>
    /// <param name="Format">Detected import format.</param>
    /// <param name="ImportedAtUtc">UTC timestamp when the file was imported.</param>
    /// <param name="OriginalFileName">Original uploaded file name.</param>
    /// <param name="TotalEntries">Number of entries parsed from the imported statement file.</param>
    /// <param name="CreatedUtc">Entity creation timestamp UTC.</param>
    /// <param name="ModifiedUtc">Entity last modified timestamp UTC, if any.</param>
    /// <returns>The result.</returns>
    public sealed record StatementImportBackupDto(Guid Id, Guid AccountId, ImportFormat Format, DateTime ImportedAtUtc, string OriginalFileName, int TotalEntries, DateTime CreatedUtc, DateTime? ModifiedUtc);

    /// <summary>
    /// Creates a backup DTO representing the serializable state of this statement import.
    /// </summary>
    /// <param name="AccountId">The account id.</param>
    /// <param name="Format">The format.</param>
    /// <param name="ImportedAtUtc">The imported at utc.</param>
    /// <param name="OriginalFileName">The original file name.</param>
    /// <param name="TotalEntries">The total entries.</param>
    /// <param name="CreatedUtc">The created utc.</param>
    /// <param name="ModifiedUtc">The modified utc.</param>
    /// <returns>A <see cref="StatementImportBackupDto"/> containing values required to restore this import.</returns>
    public StatementImportBackupDto ToBackupDto() => new StatementImportBackupDto(Id, AccountId, Format, ImportedAtUtc, OriginalFileName, TotalEntries, CreatedUtc, ModifiedUtc);

    /// <summary>
    /// Assigns values from the provided backup DTO to this <see cref="StatementImport"/> instance.
    /// </summary>
    /// <param name="dto">The <see cref="StatementImportBackupDto"/> containing values to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(StatementImportBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        AccountId = dto.AccountId;
        Format = dto.Format;
        ImportedAtUtc = dto.ImportedAtUtc;
        OriginalFileName = dto.OriginalFileName;
        TotalEntries = dto.TotalEntries;
    }
}