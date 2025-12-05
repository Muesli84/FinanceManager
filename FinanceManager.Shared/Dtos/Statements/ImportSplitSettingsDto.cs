namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// DTO representing user-configurable settings for splitting imports into drafts.
/// </summary>
public sealed class ImportSplitSettingsDto
{
    /// <summary>Selected split mode.</summary>
    public ImportSplitMode Mode { get; set; } = ImportSplitMode.MonthlyOrFixed;
    /// <summary>Maximum entries allowed per draft.</summary>
    public int MaxEntriesPerDraft { get; set; } = 250;
    /// <summary>Optional monthly split threshold; applies to hybrid mode.</summary>
    public int? MonthlySplitThreshold { get; set; } = 250;
    /// <summary>Minimum entries per monthly draft.</summary>
    public int MinEntriesPerDraft { get; set; } = 8;
}
