using FinanceManager.Application.Securities;
using FinanceManager.Domain.Securities;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Securities;

public sealed class SecurityCategoryService : ISecurityCategoryService
{
    private readonly AppDbContext _db;
    public SecurityCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SecurityCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        return await _db.SecurityCategories.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new SecurityCategoryDto { Id = c.Id, Name = c.Name, SymbolAttachmentId = c.SymbolAttachmentId })
            .ToListAsync(ct);
    }

    public async Task<SecurityCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.SecurityCategories.AsNoTracking()
            .Where(c => c.Id == id && c.OwnerUserId == ownerUserId)
            .Select(c => new SecurityCategoryDto { Id = c.Id, Name = c.Name, SymbolAttachmentId = c.SymbolAttachmentId })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SecurityCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        var category = new SecurityCategory(ownerUserId, name);
        _db.SecurityCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return new SecurityCategoryDto { Id = category.Id, Name = category.Name, SymbolAttachmentId = category.SymbolAttachmentId };
    }

    public async Task<SecurityCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct)
    {
        var category = await _db.SecurityCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (category == null) return null;
        category.Rename(name);
        await _db.SaveChangesAsync(ct);
        return new SecurityCategoryDto { Id = category.Id, Name = category.Name, SymbolAttachmentId = category.SymbolAttachmentId };
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var category = await _db.SecurityCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (category == null) return false;
        _db.SecurityCategories.Remove(category);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct)
    {
        var cat = await _db.SecurityCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (cat == null) throw new ArgumentException("Category not found", nameof(id));

        if (attachmentId.HasValue)
        {
            var exists = await _db.Attachments.AsNoTracking()
                .AnyAsync(a => a.Id == attachmentId.Value && a.OwnerUserId == ownerUserId, ct);
            if (!exists)
            {
                throw new ArgumentException("Attachment not found", nameof(attachmentId));
            }
        }

        cat.SetSymbolAttachment(attachmentId);
        await _db.SaveChangesAsync(ct);
    }
}