namespace FinanceManager.Shared.Dtos.HomeKpi;

public sealed record HomeKpiCreateRequest(
    HomeKpiKind Kind,
    Guid? ReportFavoriteId,
    HomeKpiPredefined? PredefinedType,
    string? Title,
    HomeKpiDisplayMode DisplayMode,
    int SortOrder
)
{
    public HomeKpiCreateRequest(HomeKpiKind kind, Guid? reportFavoriteId, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, null, displayMode, sortOrder) { }
}

public sealed record HomeKpiUpdateRequest(
    HomeKpiKind Kind,
    Guid? ReportFavoriteId,
    HomeKpiPredefined? PredefinedType,
    string? Title,
    HomeKpiDisplayMode DisplayMode,
    int SortOrder
)
{
    public HomeKpiUpdateRequest(HomeKpiKind kind, Guid? reportFavoriteId, string? title, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, title, displayMode, sortOrder) { }
    public HomeKpiUpdateRequest(HomeKpiKind kind, Guid? reportFavoriteId, HomeKpiDisplayMode displayMode, int sortOrder)
        : this(kind, reportFavoriteId, null, null, displayMode, sortOrder) { }
}

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
