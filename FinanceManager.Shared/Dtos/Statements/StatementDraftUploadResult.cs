namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result returned after uploading a statement draft file.
/// </summary>
public sealed record StatementDraftUploadResult(StatementDraftDto? FirstDraft, object? SplitInfo);
