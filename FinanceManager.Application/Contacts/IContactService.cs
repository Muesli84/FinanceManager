using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;

namespace FinanceManager.Application.Contacts;

public interface IContactService
{
    Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);
    Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, ContactType? type, CancellationToken ct); // erweitert
    Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task AddAliasAsync(Guid contactId, Guid ownerUserId, string pattern, CancellationToken ct);
    Task DeleteAliasAsync(Guid contactId, Guid ownerUserId, Guid aliasId, CancellationToken ct);
    Task<IReadOnlyList<AliasNameDto>> ListAliases(Guid id, Guid userId, CancellationToken ct);
}
