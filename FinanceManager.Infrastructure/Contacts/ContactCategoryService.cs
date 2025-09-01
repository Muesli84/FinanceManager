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
            .Select(c => new ContactCategoryDto(c.Id, c.Name))
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
        return new ContactCategoryDto(cat.Id, cat.Name);
    }
}