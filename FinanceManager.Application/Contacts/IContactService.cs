using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;

namespace FinanceManager.Application.Contacts;

public interface IContactService
{
    Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, CancellationToken ct);
    Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, CancellationToken ct);
    Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}
