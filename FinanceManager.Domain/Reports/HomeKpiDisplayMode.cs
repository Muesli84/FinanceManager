namespace FinanceManager.Domain.Reports;

/// <summary>
/// Defines how a KPI tile should visualize data.
/// </summary>
public enum HomeKpiDisplayMode
{
    TotalOnly = 0,
    TotalWithComparisons = 1,
    ReportGraph = 2
}
