namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Status snapshot for mass booking background task.
/// </summary>
/// <param name="Running">True while task is queued or running.</param>
/// <param name="Processed">Number of successfully booked drafts.</param>
/// <param name="Failed">Number of drafts with issues (errors or blocking warnings).</param>
/// <param name="Total">Total draft count at start.</param>
/// <param name="Warnings">Total warning messages accumulated.</param>
/// <param name="Errors">Total error messages accumulated.</param>
/// <param name="Message">Progress / status message.</param>
/// <param name="Issues">Collected issue list (may be empty when not tracked).</param>
public sealed record StatementDraftMassBookStatusDto(
    bool Running,
    int Processed,
    int Failed,
    int Total,
    int Warnings,
    int Errors,
    string? Message,
    IReadOnlyList<StatementDraftMassBookIssueDto> Issues);
