using FinanceManager.Shared.Dtos;

namespace FinanceManager.Application.Reports;

/// <summary>
/// CRUD operations for user-scoped Home KPI configurations (FA-KPI-007).
/// </summary>
public interface IHomeKpiService
{
    Task<IReadOnlyList<HomeKpiDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<HomeKpiDto> CreateAsync(Guid ownerUserId, HomeKpiCreateRequest request, CancellationToken ct);
    Task<HomeKpiDto?> UpdateAsync(Guid id, Guid ownerUserId, HomeKpiUpdateRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
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
