using FinanceManager.Application.Contacts;
using FinanceManager.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Contacts;

public sealed class ContactCategoryService : IContactCategoryService
{
    private readonly AppDbContext _db;
    public ContactCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ContactCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        return await _db.Set<ContactCategory>().AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new ContactCategoryDto(c.Id, c.Name, c.SymbolAttachmentId))
            .ToListAsync(ct);
    }

    public async Task<ContactCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        var exists = await _db.Set<ContactCategory>()
            .AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name, ct);
        if (exists)
        {
            throw new ArgumentException("Category name already exists.");
        }

        var cat = new ContactCategory(ownerUserId, name);
        _db.Add(cat);
        await _db.SaveChangesAsync(ct);
        return new ContactCategoryDto(cat.Id, cat.Name, cat.SymbolAttachmentId);
    }

    public async Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct)
    {
        var cat = await _db.Set<ContactCategory>()
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (cat == null) throw new ArgumentException("Category not found", nameof(id));
        cat.SetSymbolAttachment(attachmentId);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ContactCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var c = await _db.Set<ContactCategory>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (c == null) return null;
        return new ContactCategoryDto(c.Id, c.Name, c.SymbolAttachmentId);
    }

    public async Task UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct)
    {
        var c = await _db.Set<ContactCategory>()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (c == null) throw new ArgumentException("Category not found", nameof(id));
        c.Rename(name);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var c = await _db.Set<ContactCategory>()
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerUserId, ct);
        if (c == null) throw new ArgumentException("Category not found", nameof(id));
        _db.Remove(c);
        await _db.SaveChangesAsync(ct);
    }
}