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

