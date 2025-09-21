using FinanceManager.Shared.Dtos;

public interface ISavingsPlanCategoryService
{
    Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<SavingsPlanCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct); // fetch single category
    Task<SavingsPlanCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
    Task<SavingsPlanCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}