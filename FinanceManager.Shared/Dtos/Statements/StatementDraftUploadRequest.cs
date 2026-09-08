using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Statements;

/// <summary>
/// Request payload describing a statement draft upload operation.
/// </summary>
/// <param name="FileName">The file name.</param>
/// <returns>The result.</returns>
public sealed record StatementDraftUploadRequest([property: Required] string FileName);
