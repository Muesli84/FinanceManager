using FinanceManager.Application.Securities;
using FinanceManager.Domain.Securities;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Securities;

public sealed class SecurityCategoryService : ISecurityCategoryService
{
    private readonly AppDbContext _db;
    public SecurityCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SecurityCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.SecurityCategories
            .AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new SecurityCategoryDto { Id = c.Id, Name = c.Name })
            .ToListAsync(ct);

    public async Task<SecurityCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var cat = await _db.SecurityCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        return cat == null ? null : new SecurityCategoryDto { Id = cat.Id, Name = cat.Name };
    }

    public async Task<SecurityCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        var exists = await _db.SecurityCategories.AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name, ct);
        if (exists) { throw new ArgumentException("Category name must be unique per user", nameof(name)); }
        var entity = new SecurityCategory(ownerUserId, name);
        _db.SecurityCategories.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new SecurityCategoryDto { Id = entity.Id, Name = entity.Name };
    }

    public async Task<SecurityCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct)
    {
        var entity = await _db.SecurityCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return null; }
        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.SecurityCategories.AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name && c.Id != id, ct);
            if (exists) { throw new ArgumentException("Category name must be unique per user", nameof(name)); }
        }
        entity.Rename(name);
        await _db.SaveChangesAsync(ct);
        return new SecurityCategoryDto { Id = entity.Id, Name = entity.Name };
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.SecurityCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return false; }

        // Optional: Prüfen ob Securities diese Kategorie benutzen (falls später Feld CategoryId in Security ergänzt wird).
        _db.SecurityCategories.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}