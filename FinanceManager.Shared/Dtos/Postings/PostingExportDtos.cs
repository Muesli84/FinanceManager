namespace FinanceManager.Shared.Dtos.Postings;

public sealed record PostingExportRequest(
    string? Format = "csv",
    DateTime? From = null,
    DateTime? To = null,
    string? Q = null
);
