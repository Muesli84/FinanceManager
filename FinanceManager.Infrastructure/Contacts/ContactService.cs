using FinanceManager.Application.Contacts;
using FinanceManager.Domain;
using FinanceManager.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Contacts;

public sealed class ContactService : IContactService
{
    private readonly AppDbContext _db;
    public ContactService(AppDbContext db) { _db = db; }

    public async Task<ContactDto> CreateAsync(Guid ownerUserId, string name, ContactType type, Guid? categoryId, CancellationToken ct)
    {
        var contact = new Contact(ownerUserId, name, type, categoryId);
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(ct);
        return new ContactDto(contact.Id, contact.Name, contact.Type, contact.CategoryId);
    }

    public async Task<ContactDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, ContactType type, Guid? categoryId, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) return null;
        contact.Rename(name);
        contact.ChangeType(type);
        contact.SetCategory(categoryId);
        await _db.SaveChangesAsync(ct);
        return new ContactDto(contact.Id, contact.Name, contact.Type, contact.CategoryId);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (contact == null) return false;
        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ContactDto>> ListAsync(Guid ownerUserId, int skip, int take, CancellationToken ct)
    {
        return await _db.Contacts.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Skip(skip).Take(take)
            .Select(c => new ContactDto(c.Id, c.Name, c.Type, c.CategoryId))
            .ToListAsync(ct);
    }

    public async Task<ContactDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.Contacts.AsNoTracking()
            .Where(c => c.Id == id && c.OwnerUserId == ownerUserId)
            .Select(c => new ContactDto(c.Id, c.Name, c.Type, c.CategoryId))
            .FirstOrDefaultAsync(ct);
    }
}
