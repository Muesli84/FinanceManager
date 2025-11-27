namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO wrapping an API error message for standardized responses.
/// </summary>
/// <param name="error">Short error message intended for display.</param>
public sealed record ApiErrorDto(string error);
