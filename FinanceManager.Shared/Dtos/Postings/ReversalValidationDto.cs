namespace FinanceManager.Shared.Dtos.Postings;

/// <summary>
/// Validation result for a posting reversal request.
/// </summary>
/// <param name="IsValid">Indicates whether the reversal can proceed.</param>
/// <param name="Errors">List of validation errors if the reversal cannot proceed.</param>
/// <returns>The result.</returns>
public sealed record ReversalValidationDto(
    bool IsValid,
    IReadOnlyList<string> Errors);
