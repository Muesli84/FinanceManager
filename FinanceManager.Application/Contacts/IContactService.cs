using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Application.Contacts;

public interface IContactService
{
    Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);
    Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description, bool? isPaymentIntermediary, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, ContactType? type, string? nameFilter, CancellationToken ct); // nameFilter NEU
    Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task AddAliasAsync(Guid contactId, Guid ownerUserId, string pattern, CancellationToken ct);
    Task DeleteAliasAsync(Guid contactId, Guid ownerUserId, Guid aliasId, CancellationToken ct);
    Task<IReadOnlyList<AliasNameDto>> ListAliases(Guid id, Guid userId, CancellationToken ct);
    Task<ContactDto> MergeAsync(Guid ownerUserId, Guid sourceContactId, Guid targetContactId, CancellationToken ct);
}
