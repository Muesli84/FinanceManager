namespace FinanceManager.Application.Reports;

/// <summary>
/// CRUD operations for user scoped report favorites (FA-REP-008).
/// </summary>
public interface IReportFavoriteService
{
    Task<IReadOnlyList<ReportFavoriteDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<ReportFavoriteDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<ReportFavoriteDto> CreateAsync(Guid ownerUserId, ReportFavoriteCreateRequest request, CancellationToken ct);
    Task<ReportFavoriteDto?> UpdateAsync(Guid id, Guid ownerUserId, ReportFavoriteUpdateRequest request, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}


