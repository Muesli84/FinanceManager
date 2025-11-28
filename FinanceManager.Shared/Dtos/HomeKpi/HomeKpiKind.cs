namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// Identifies the type/source of a KPI tile on the home dashboard.
/// </summary>
public enum HomeKpiKind
{
    /// <summary>A predefined KPI provided by the application.</summary>
    Predefined = 0,
    /// <summary>A KPI based on a user-defined report favorite.</summary>
    ReportFavorite = 1
}
