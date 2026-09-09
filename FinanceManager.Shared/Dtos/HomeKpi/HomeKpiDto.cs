namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// DTO representing a KPI tile displayed on the home dashboard.
/// </summary>
/// <param name="Id">Unique KPI identifier.</param>
/// <param name="Kind">Kind/source of the KPI.</param>
/// <param name="ReportFavoriteId">Optional linked report favorite identifier.</param>
/// <param name="ReportFavoriteName">Optional linked report favorite name.</param>
/// <param name="Title">Optional custom title override.</param>
/// <param name="PredefinedType">Optional predefined KPI type.</param>
/// <param name="DisplayMode">Display mode of the KPI tile.</param>
/// <param name="SortOrder">Sort order for placement on the dashboard.</param>
/// <param name="CreatedUtc">UTC timestamp when the KPI was created.</param>
/// <param name="ModifiedUtc">UTC timestamp when the KPI was last modified, if any.</param>
/// <returns>The result.</returns>
public sealed record HomeKpiDto(
    Guid Id,
    HomeKpiKind Kind,
    Guid? ReportFavoriteId,
    string? ReportFavoriteName,
    string? Title,
    HomeKpiPredefined? PredefinedType,
    HomeKpiDisplayMode DisplayMode,
    int SortOrder,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc
);
