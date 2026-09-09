namespace FinanceManager.Shared.Dtos.Common;

/// <summary>
/// DTO wrapping an API error response for standardized client consumption.
/// </summary>
/// <param name="origin">Origin identifier (e.g. API_BudgetRule) used to scope error codes for localization.</param>
/// <param name="code">Machine-readable error code (e.g. Err_Invalid_BudgetPurposeId).</param>
/// <param name="message">Human-readable error message intended for display (preferably localized server-side).</param>
/// <returns>The result.</returns>
public sealed record ApiErrorDto(string? origin, string? code, string? message)
{
    /// <summary>
    /// Legacy compatibility property. Older clients expect an <c>error</c> JSON property.
    /// </summary>
    public string? Error => code;

    /// <summary>
    /// Creates a standardized API error response.
    /// </summary>
    /// <param name="origin">The origin.</param>
    /// <param name="code">The code.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    public static ApiErrorDto Create(string origin, string code, string? message)
        => new(origin, code, message);
}
