namespace FinanceManager.Shared.Dtos.Common;

/// <summary>
/// DTO indicating the current status of an aggregates rebuild operation.
/// </summary>
/// <param name="Running">True when the rebuild task is currently running.</param>
/// <param name="Processed">Number of processed items so far.</param>
/// <param name="Total">Total number of items to process.</param>
/// <param name="Message">Optional status message.</param>
public sealed record AggregatesRebuildStatusDto(bool Running, int Processed, int Total, string? Message);
