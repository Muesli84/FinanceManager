using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request payload to update a user's import split settings used during statement draft creation.
/// </summary>
/// <param name="Mode">The mode.</param>
/// <param name="MaxEntriesPerDraft">The max entries per draft.</param>
/// <param name="MonthlySplitThreshold">The monthly split threshold.</param>
/// <param name="MinEntriesPerDraft">The min entries per draft.</param>
/// <param name="MassImportDialogPolicy">The mass import dialog policy.</param>
/// <param name="KnownContactAutoCreateEnabled">The known contact auto create enabled.</param>
/// <returns>The result.</returns>
public sealed record ImportSplitSettingsUpdateRequest(
    [param: Required] ImportSplitMode Mode,
    [param: Range(20, 10000)] int MaxEntriesPerDraft,
    int? MonthlySplitThreshold,
    [param: Range(1, 10000)] int MinEntriesPerDraft,
    [param: Required] MassImportDialogPolicy MassImportDialogPolicy,
    bool KnownContactAutoCreateEnabled = true
);
