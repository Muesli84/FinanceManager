namespace FinanceManager.Shared.Dtos;
/// <summary>
/// DTO representing a single aggregate time series point returned by reporting endpoints.
/// </summary>
/// <param name="PeriodStart">Start of the period represented by the point (UTC date aligned to the period).</param>
/// <param name="Amount">Aggregated amount for the period.</param>
public sealed record TimeSeriesPointDto(System.DateTime PeriodStart, decimal Amount);
