namespace FinanceManager.Application.Contacts;

public interface IContactCategoryService
{
    Task<IReadOnlyList<ContactCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<ContactCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
}