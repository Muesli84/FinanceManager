using FinanceManager.Domain;

namespace FinanceManager.Application.Accounts;

public interface IAccountService
{
    Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, CancellationToken ct);
    Task<AccountDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string? iban, Guid bankContactId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<IReadOnlyList<AccountDto>> ListAsync(Guid ownerUserId, int skip, int take, CancellationToken ct);
    Task<AccountDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}

public sealed record AccountDto(Guid Id, string Name, AccountType Type, string? Iban, decimal CurrentBalance, Guid BankContactId);
