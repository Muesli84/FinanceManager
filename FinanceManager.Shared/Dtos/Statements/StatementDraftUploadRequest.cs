using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request payload describing a statement draft upload operation.
/// </summary>
public sealed record StatementDraftUploadRequest([property: Required] string FileName);
