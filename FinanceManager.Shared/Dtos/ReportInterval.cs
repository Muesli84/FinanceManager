namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Interval granularity for configurable aggregate reports. YTD represents a cumulative year-to-date view.
/// </summary>
public enum ReportInterval
{
    /// <summary>Aggregate by calendar month.</summary>
    Month = 0,
    /// <summary>Aggregate by calendar quarter.</summary>
    Quarter = 1,
    /// <summary>Aggregate by half-year (two quarters).</summary>
    HalfYear = 2,
    /// <summary>Aggregate by calendar year.</summary>
    Year = 3,
    /// <summary>Year-to-date cumulative view.</summary>
    Ytd = 4,
    /// <summary>All history cumulative view.</summary>
    AllHistory = 5
}
