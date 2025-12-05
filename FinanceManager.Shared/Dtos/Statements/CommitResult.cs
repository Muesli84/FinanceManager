namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Result information after committing a draft into a statement import.
/// </summary>
/// <param name="StatementImportId">Created statement import id.</param>
/// <param name="TotalEntries">Number of entries committed.</param>
public sealed record CommitResult(Guid StatementImportId, int TotalEntries);
