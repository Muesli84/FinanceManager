namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO representing a daily closing price for a security.
/// </summary>
/// <param name="Date">Date of the closing price.</param>
/// <param name="Close">Closing price amount.</param>
public sealed record SecurityPriceDto(DateTime Date, decimal Close);
