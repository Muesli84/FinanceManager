namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// Request payload to update an existing KPI tile for the home dashboard.
/// </summary>
/// <param name="Kind">Kind/source of the KPI.</param>
/// <param name="ReportFavoriteId">Optional linked report favorite identifier.</param>
/// <param name="PredefinedType">Optional predefined KPI type.</param>
/// <param name="Title">Optional custom title override.</param>
/// <param name="DisplayMode">Display mode of the KPI tile.</param>
/// <param name="SortOrder">Sort order for placement on the dashboard.</param>
/// <returns>The result.</returns>
public sealed record HomeKpiUpdateRequest(
    HomeKpiKind Kind,
    Guid? ReportFavoriteId,
    HomeKpiPredefined? PredefinedType,
    string? Title,
    HomeKpiDisplayMode DisplayMode,
    int SortOrder
)
{
    /// <summary>
    /// Convenience constructor to update a KPI with title and without predefined type.
    /// </summary>
    /// <param name="kind">Kind/source of the KPI.</param>
    /// <param name="reportFavoriteId">Optional linked report favorite identifier.</param>
    /// <param name="title">Optional custom title override.</param>
    /// <param name="displayMode">Display mode of the KPI tile.</param>
    /// <param name="sortOrder">Sort order for placement on the dashboard.</param>
    public HomeKpiUpdateRequest(HomeKpiKind kind, Guid? reportFavoriteId, string? title, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, title, displayMode, sortOrder) { }

    /// <summary>
    /// Convenience constructor to update a KPI without predefined type and title.
    /// </summary>
    /// <param name="kind">Kind/source of the KPI.</param>
    /// <param name="reportFavoriteId">Optional linked report favorite identifier.</param>
    /// <param name="displayMode">Display mode of the KPI tile.</param>
    /// <param name="sortOrder">Sort order for placement on the dashboard.</param>
    public HomeKpiUpdateRequest(HomeKpiKind kind, Guid? reportFavoriteId, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, null, displayMode, sortOrder) { }
}
