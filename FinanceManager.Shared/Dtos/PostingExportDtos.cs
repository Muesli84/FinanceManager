using System;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload used to export postings from a context (account, contact, savings plan, or security).
/// </summary>
/// <param name="Format">Export format, e.g., "csv" or "xlsx".</param>
/// <param name="From">Optional start date filter.</param>
/// <param name="To">Optional end date filter.</param>
/// <param name="Q">Optional search query string.</param>
public sealed record PostingExportRequest(
    string? Format = "csv",
    DateTime? From = null,
    DateTime? To = null,
    string? Q = null
);