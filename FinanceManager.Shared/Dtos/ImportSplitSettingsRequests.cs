using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to update a user's import split settings used during statement draft creation.
/// </summary>
/// <param name="Mode">The split mode to use (FixedSize, Monthly, or MonthlyOrFixed).</param>
/// <param name="MaxEntriesPerDraft">Maximum number of entries allowed per draft.</param>
/// <param name="MonthlySplitThreshold">Optional monthly threshold defining when monthly split applies.</param>
/// <param name="MinEntriesPerDraft">Minimum number of entries required per draft when not in FixedSize mode.</param>
public sealed record ImportSplitSettingsUpdateRequest(
    [property: Required] ImportSplitMode Mode,
    [property: Range(20, 10000)] int MaxEntriesPerDraft,
    int? MonthlySplitThreshold,
    [property: Range(1, 10000)] int MinEntriesPerDraft
);
