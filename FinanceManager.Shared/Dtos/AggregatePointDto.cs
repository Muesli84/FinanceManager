namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO representing a single aggregate data point for charts and summaries.
/// </summary>
/// <param name="PeriodStart">Start date of the aggregation period.</param>
/// <param name="Amount">Aggregated amount for the period.</param>
public sealed record AggregatePointDto(DateTime PeriodStart, decimal Amount);
