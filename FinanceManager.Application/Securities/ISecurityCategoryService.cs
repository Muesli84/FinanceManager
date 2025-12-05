namespace FinanceManager.Application.Securities;

public interface ISecurityCategoryService
{
    Task<IReadOnlyList<SecurityCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<SecurityCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<SecurityCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
    Task<SecurityCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);
}