namespace FinanceManager.Shared.Dtos.Budget;

/// <summary>
/// Request for exporting all postings of a budget report across the full report range.
/// </summary>
/// <param name="AsOfDate">The as of date.</param>
/// <param name="Months">The months.</param>
/// <param name="DateBasis">The date basis.</param>
/// <returns>The result.</returns>
public sealed record BudgetReportExportRequest(
    DateOnly AsOfDate,
    int Months,
    BudgetReportDateBasis DateBasis
);
