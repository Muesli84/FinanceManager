namespace FinanceManager.Domain.Reports;

/// <summary>
/// Interval granularity for configurable aggregate reports. YTD represents a cumulative year-to-date view.
/// </summary>
public enum ReportInterval
{
    Month = 0,
    Quarter = 1,
    HalfYear = 2,
    Year = 3,
    Ytd = 4,
    AllHistory = 5
}
