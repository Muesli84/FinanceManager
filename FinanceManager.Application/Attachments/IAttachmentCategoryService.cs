namespace FinanceManager.Application.Attachments;

public interface IAttachmentCategoryService
{
    Task<IReadOnlyList<AttachmentCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
    // Allow creating system categories from infrastructure/controller
    Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, bool isSystem, CancellationToken ct);
    Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct);
    Task<AttachmentCategoryDto?> UpdateAsync(Guid ownerUserId, Guid id, string name, CancellationToken ct);
}
