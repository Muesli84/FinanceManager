namespace FinanceManager.Shared.Dtos;

public sealed record BackupRestoreStatusDto(
    bool Running,
    int Processed,
    int Total,
    string? Message,
    string? Error,
    int Processed2,
    int Total2,
    string? Message2);
