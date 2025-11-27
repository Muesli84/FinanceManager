namespace FinanceManager.Shared.Dtos.Statements;

public sealed class ImportSplitSettingsDto
{
    public ImportSplitMode Mode { get; set; } = ImportSplitMode.MonthlyOrFixed;
    public int MaxEntriesPerDraft { get; set; } = 250;
    public int? MonthlySplitThreshold { get; set; } = 250;
    public int MinEntriesPerDraft { get; set; } = 8;
}
