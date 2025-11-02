namespace FinanceManager.Application.Contacts;

public interface IContactCategoryService
{
    Task<IReadOnlyList<ContactCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<ContactCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);

    // Detail operations
    Task<ContactCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct);
    Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}