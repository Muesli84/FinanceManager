using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record ImportSplitSettingsUpdateRequest(
    [property: Required] ImportSplitMode Mode,
    [property: Range(20, 10000)] int MaxEntriesPerDraft,
    int? MonthlySplitThreshold,
    [property: Range(1, 10000)] int MinEntriesPerDraft
);
