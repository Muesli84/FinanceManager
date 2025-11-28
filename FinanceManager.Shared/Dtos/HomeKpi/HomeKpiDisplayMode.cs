namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// Defines how a KPI tile should visualize data.
/// </summary>
public enum HomeKpiDisplayMode
{
    /// <summary>Show the total value only.</summary>
    TotalOnly = 0,
    /// <summary>Show the total value including comparison metrics.</summary>
    TotalWithComparisons = 1,
    /// <summary>Show a small report graph instead of a total.</summary>
    ReportGraph = 2
}
