namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Response payload returned after uploading a statement draft file.
/// </summary>
/// <param name="FirstDraft">First created draft, when any.</param>
/// <param name="SplitInfo">Optional split information for the upload.</param>
public sealed record StatementDraftUploadResult(StatementDraftDto? FirstDraft, ImportSplitInfoDto? SplitInfo);
