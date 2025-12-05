using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request payload to update a user's import split settings used during statement draft creation.
/// </summary>
public sealed record ImportSplitSettingsUpdateRequest(
    [param: Required] ImportSplitMode Mode,
    [param: Range(20, 10000)] int MaxEntriesPerDraft,
    int? MonthlySplitThreshold,
    [param: Range(1, 10000)] int MinEntriesPerDraft
);
