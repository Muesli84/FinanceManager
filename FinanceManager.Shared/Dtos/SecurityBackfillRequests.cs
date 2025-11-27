namespace FinanceManager.Shared.Dtos;

public sealed record SecurityBackfillRequest(Guid? SecurityId, DateTime? FromDateUtc, DateTime? ToDateUtc);
