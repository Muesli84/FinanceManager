namespace FinanceManager.Shared.Dtos.HomeKpi;

/// <summary>
/// DTO representing a KPI tile displayed on the home dashboard.
/// </summary>
public sealed record HomeKpiDto(
    /// <summary>Unique KPI identifier.</summary>
    Guid Id,
    /// <summary>Kind/source of the KPI.</summary>
    HomeKpiKind Kind,
    /// <summary>Optional linked report favorite identifier.</summary>
    Guid? ReportFavoriteId,
    /// <summary>Optional linked report favorite name.</summary>
    string? ReportFavoriteName,
    /// <summary>Optional custom title override.</summary>
    string? Title,
    /// <summary>Optional predefined KPI type.</summary>
    HomeKpiPredefined? PredefinedType,
    /// <summary>Display mode of the KPI tile.</summary>
    HomeKpiDisplayMode DisplayMode,
    /// <summary>Sort order for placement on the dashboard.</summary>
    int SortOrder,
    /// <summary>UTC timestamp when the KPI was created.</summary>
    DateTime CreatedUtc,
    /// <summary>UTC timestamp when the KPI was last modified, if any.</summary>
    DateTime? ModifiedUtc
);
