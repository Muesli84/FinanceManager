namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to trigger backfill of security prices.
/// </summary>
/// <param name="SecurityId">Optional security identifier to limit the backfill to a single security.</param>
/// <param name="FromDateUtc">Optional start date (UTC) for backfill.</param>
/// <param name="ToDateUtc">Optional end date (UTC) for backfill.</param>
public sealed record SecurityBackfillRequest(Guid? SecurityId, DateTime? FromDateUtc, DateTime? ToDateUtc);
