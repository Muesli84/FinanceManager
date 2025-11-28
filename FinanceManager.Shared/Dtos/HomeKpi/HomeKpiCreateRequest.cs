namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// Request payload to create a KPI tile for the home dashboard.
/// </summary>
public sealed record HomeKpiCreateRequest(
    /// <summary>Kind/source of the KPI.</summary>
    HomeKpiKind Kind,
    /// <summary>Optional linked report favorite identifier.</summary>
    Guid? ReportFavoriteId,
    /// <summary>Optional predefined KPI type.</summary>
    HomeKpiPredefined? PredefinedType,
    /// <summary>Optional custom title override.</summary>
    string? Title,
    /// <summary>Display mode of the KPI tile.</summary>
    HomeKpiDisplayMode DisplayMode,
    /// <summary>Sort order for placement on the dashboard.</summary>
    int SortOrder
)
{
    /// <summary>
    /// Convenience constructor to create a KPI without predefined type or title.
    /// </summary>
    /// <param name="kind">Kind/source of the KPI.</param>
    /// <param name="reportFavoriteId">Optional linked report favorite identifier.</param>
    /// <param name="displayMode">Display mode of the KPI tile.</param>
    /// <param name="sortOrder">Sort order for placement on the dashboard.</param>
    public HomeKpiCreateRequest(HomeKpiKind kind, Guid? reportFavoriteId, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, null, displayMode, sortOrder) { }
}
