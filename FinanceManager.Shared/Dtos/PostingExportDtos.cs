using System;

namespace FinanceManager.Shared.Dtos;

public sealed record PostingExportRequest(
    string? Format = "csv",
    DateTime? From = null,
    DateTime? To = null,
    string? Q = null
);