namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Summary about how an uploaded statement file was split into drafts.
/// </summary>
/// <param name="Mode">Configured split mode.</param>
/// <param name="EffectiveMonthly">True when monthly splitting was used effectively.</param>
/// <param name="DraftCount">Number of created drafts.</param>
/// <param name="TotalMovements">Total detected movements in the file.</param>
/// <param name="MaxEntriesPerDraft">Maximum entries per draft according to settings.</param>
/// <param name="LargestDraftSize">Largest draft size in number of entries.</param>
/// <param name="MonthlyThreshold">Monthly split threshold used for hybrid mode.</param>
public sealed record ImportSplitInfoDto(
    string Mode,
    bool EffectiveMonthly,
    int DraftCount,
    int TotalMovements,
    int MaxEntriesPerDraft,
    int LargestDraftSize,
    int MonthlyThreshold
);
