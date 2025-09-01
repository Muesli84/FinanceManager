using FinanceManager.Application.Accounts;
using FinanceManager.Domain;
using FinanceManager.Domain.Accounts;
using FinanceManager.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Accounts;

public sealed class AccountService : IAccountService
{
    private readonly AppDbContext _db;
    public AccountService(AppDbContext db) => _db = db;

    public async Task<AccountDto> CreateAsync(Guid ownerUserId, string name, AccountType type, string? iban, Guid bankContactId, CancellationToken ct)
    {
        if (!await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == bankContactId && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank, ct))
        {
            throw new ArgumentException("Bank contact invalid", nameof(bankContactId));
        }
        if (!string.IsNullOrWhiteSpace(iban))
        {
            var norm = iban.Trim();
            bool exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Iban == norm, ct);
            if (exists)
            {
                throw new ArgumentException("IBAN already exists for user", nameof(iban));
            }
            iban = norm;
        }
        var account = new Account(ownerUserId, type, name, iban, bankContactId);
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(ct);
        return new AccountDto(account.Id, account.Name, account.Type, account.Iban, account.CurrentBalance, account.BankContactId);
    }

    public async Task<AccountDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string? iban, Guid bankContactId, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);
        if (account == null) return null;
        if (!await _db.Contacts.AsNoTracking().AnyAsync(c => c.Id == bankContactId && c.OwnerUserId == ownerUserId && c.Type == ContactType.Bank, ct))
        {
            throw new ArgumentException("Bank contact invalid", nameof(bankContactId));
        }
        if (!string.IsNullOrWhiteSpace(iban))
        {
            iban = iban.Trim();
            bool exists = await _db.Accounts.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.Iban == iban && a.Id != id, ct);
            if (exists) throw new ArgumentException("IBAN already exists for user", nameof(iban));
        }
        account.Rename(name);
        account.SetIban(iban);
        account.SetBankContact(bankContactId);
        await _db.SaveChangesAsync(ct);
        return new AccountDto(account.Id, account.Name, account.Type, account.Iban, account.CurrentBalance, account.BankContactId);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.OwnerUserId == ownerUserId, ct);
        if (account == null) return false;
        var bankContactId = account.BankContactId;
        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync(ct);
        bool anyOther = await _db.Accounts.AsNoTracking().AnyAsync(a => a.BankContactId == bankContactId, ct);
        if (!anyOther)
        {
            var bankContact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == bankContactId && c.Type == ContactType.Bank, ct);
            if (bankContact != null)
            {
                _db.Contacts.Remove(bankContact);
                await _db.SaveChangesAsync(ct);
            }
        }
        return true;
    }

    public async Task<IReadOnlyList<AccountDto>> ListAsync(Guid ownerUserId, int skip, int take, CancellationToken ct)
    {
        return await _db.Accounts.AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .OrderBy(a => a.Name)
            .Skip(skip).Take(take)
            .Select(a => new AccountDto(a.Id, a.Name, a.Type, a.Iban, a.CurrentBalance, a.BankContactId))
            .ToListAsync(ct);
    }

    public async Task<AccountDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.Accounts.AsNoTracking()
            .Where(a => a.Id == id && a.OwnerUserId == ownerUserId)
            .Select(a => new AccountDto(a.Id, a.Name, a.Type, a.Iban, a.CurrentBalance, a.BankContactId))
            .FirstOrDefaultAsync(ct);
    }
}
