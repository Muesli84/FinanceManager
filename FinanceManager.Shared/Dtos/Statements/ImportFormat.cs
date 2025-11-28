namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Describes the original import file format used for a statement draft.
/// </summary>
public enum ImportFormat
{
    /// <summary>Comma-separated values.</summary>
    Csv = 0,
    /// <summary>Portable document format.</summary>
    Pdf = 1,
    /// <summary>Backup NDJSON format.</summary>
    Backup = 2
}
