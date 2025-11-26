namespace FinanceManager.Shared.Dtos;

public sealed record AggregatesRebuildStatusDto(bool Running, int Processed, int Total, string? Message);
