using FinanceManager.Shared.Dtos.Securities;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Controls when the mass import confirmation dialog is required.
/// </summary>
public enum MassImportDialogPolicy : short
{
    /// <summary>
    /// Always show the dialog before import execution.
    /// </summary>
    AlwaysConfirm = 0,

    /// <summary>
    /// Show the dialog only when required information is missing.
    /// </summary>
    OnMissingInformation = 1
}

/// <summary>
/// Recognized type of an uploaded mass import file.
/// </summary>
public enum MassImportFileType : short
{
    /// <summary>
    /// File type could not be recognized.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Account statement file.
    /// </summary>
    AccountStatement = 1,

    /// <summary>
    /// Security prices file.
    /// </summary>
    SecurityPrices = 2
}

/// <summary>
/// Execution status of a single file in a mass import batch.
/// </summary>
public enum MassImportFileExecutionStatus : short
{
    /// <summary>
    /// File is waiting for a confirmation decision.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// File was skipped (excluded or not importable).
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// File was imported successfully.
    /// </summary>
    Imported = 2,

    /// <summary>
    /// File import failed.
    /// </summary>
    Failed = 3
}

/// <summary>
/// Decision source for a selected file configuration.
/// </summary>
public enum MassImportDecisionSource : short
{
    /// <summary>
    /// Values were auto-detected by the system.
    /// </summary>
    AutoDetected = 0,

    /// <summary>
    /// Values were explicitly set by the user.
    /// </summary>
    UserConfirmed = 1
}

/// <summary>
/// Upload payload for one file in a mass import batch.
/// </summary>
public sealed class MassImportFileUploadDto
{
    /// <summary>
    /// Stable file identifier across analysis and confirmation calls.
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// Original file name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Optional content type.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Raw file bytes.
    /// </summary>
    public byte[] Content { get; set; } = [];
}

/// <summary>
/// User decision for one file in the confirmation dialog.
/// </summary>
public sealed class MassImportFileDecisionDto
{
    /// <summary>
    /// File identifier this decision belongs to.
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// True when the file should be excluded.
    /// </summary>
    public bool Excluded { get; set; }

    /// <summary>
    /// Selected security id for security price files.
    /// </summary>
    public Guid? SelectedSecurityId { get; set; }
}

/// <summary>
/// Request to analyze or execute a mass import batch.
/// </summary>
public sealed class MassImportBatchRequestDto
{
    /// <summary>
    /// Dialog policy used for this batch.
    /// </summary>
    public MassImportDialogPolicy DialogPolicy { get; set; } = MassImportDialogPolicy.OnMissingInformation;

    /// <summary>
    /// True when the user confirms execution.
    /// </summary>
    public bool ConfirmExecution { get; set; }

    /// <summary>
    /// Uploaded files.
    /// </summary>
    public IReadOnlyList<MassImportFileUploadDto> Files { get; set; } = [];

    /// <summary>
    /// Optional per-file dialog decisions.
    /// </summary>
    public IReadOnlyList<MassImportFileDecisionDto> Decisions { get; set; } = [];
}

/// <summary>
/// File-level result for a mass import batch.
/// </summary>
public sealed class MassImportBatchFileResultDto
{
    /// <summary>
    /// File identifier.
    /// </summary>
    public Guid FileId { get; set; }

    /// <summary>
    /// File name.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Detected file type.
    /// </summary>
    public MassImportFileType FileType { get; set; }

    /// <summary>
    /// Technical key of the import service.
    /// </summary>
    public string ServiceKey { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the import service.
    /// </summary>
    public string ServiceDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the file can be imported.
    /// </summary>
    public bool CanImport { get; set; }

    /// <summary>
    /// Whether this file is excluded from import.
    /// </summary>
    public bool Excluded { get; set; }

    /// <summary>
    /// Selected security id for security price files.
    /// </summary>
    public Guid? SelectedSecurityId { get; set; }

    /// <summary>
    /// True when the selected security was auto-guessed.
    /// </summary>
    public bool SecurityAutoGuessed { get; set; }

    /// <summary>
    /// Decision source.
    /// </summary>
    public MassImportDecisionSource DecisionSource { get; set; }

    /// <summary>
    /// Execution status for this file.
    /// </summary>
    public MassImportFileExecutionStatus ExecutionStatus { get; set; }

    /// <summary>
    /// Optional validation or failure message.
    /// </summary>
    public string? ValidationMessage { get; set; }

    /// <summary>
    /// First created statement draft id for statement imports.
    /// </summary>
    public Guid? StatementDraftId { get; set; }

    /// <summary>
    /// All created statement draft ids for statement imports (including collection statement imports with multiple drafts).
    /// </summary>
    /// <typeparam name="Guid">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public IReadOnlyList<Guid> StatementDraftIds { get; set; } = Array.Empty<Guid>();

    /// <summary>
    /// Price import counters for security files.
    /// </summary>
    public SecurityPriceImportResultDto? PriceImportResult { get; set; }
}

/// <summary>
/// Result of mass import analysis and optional execution.
/// </summary>
public sealed class MassImportBatchResultDto
{
    /// <summary>
    /// Batch identifier.
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>
    /// True when dialog is required.
    /// </summary>
    public bool DialogRequired { get; set; }

    /// <summary>
    /// True when dialog was skipped by policy.
    /// </summary>
    public bool DialogSkipped { get; set; }

    /// <summary>
    /// True when the response is analysis-only and waits for confirmation.
    /// </summary>
    public bool RequiresConfirmation { get; set; }

    /// <summary>
    /// Per-file results.
    /// </summary>
    public IReadOnlyList<MassImportBatchFileResultDto> Files { get; set; } = [];
}
