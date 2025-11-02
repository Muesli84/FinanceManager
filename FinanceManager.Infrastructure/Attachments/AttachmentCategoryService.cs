using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinanceManager.Application.Attachments;
using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Attachments;

public sealed class AttachmentCategoryService : IAttachmentCategoryService
{
    private readonly AppDbContext _db;

    public AttachmentCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<AttachmentCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.AttachmentCategories.AsNoTracking()
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .Select(c => new AttachmentCategoryDto(c.Id, c.Name, c.IsSystem,
                _db.Attachments.AsNoTracking().Any(a => a.OwnerUserId == ownerUserId && a.CategoryId == c.Id)))
            .ToListAsync(ct);

    public async Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
        => await CreateAsync(ownerUserId, name, isSystem: false, ct);

    public async Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, bool isSystem, CancellationToken ct)
    {
        var exists = await _db.AttachmentCategories.AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name, ct);
        if (exists) { throw new ArgumentException("Category name already exists"); }
        var cat = new AttachmentCategory(ownerUserId, name, isSystem);
        _db.AttachmentCategories.Add(cat);
        await _db.SaveChangesAsync(ct);
        return new AttachmentCategoryDto(cat.Id, cat.Name, cat.IsSystem, false);
    }

    public async Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct)
    {
        var anyUse = await _db.Attachments.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.CategoryId == id, ct);
        if (anyUse) { throw new InvalidOperationException("Category is in use"); }
        var cat = await _db.AttachmentCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (cat == null) { return false; }
        if (cat.IsSystem) { throw new InvalidOperationException("System category cannot be deleted"); }
        _db.AttachmentCategories.Remove(cat);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<AttachmentCategoryDto?> UpdateAsync(Guid ownerUserId, Guid id, string name, CancellationToken ct)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length < 2) { throw new ArgumentException("Name too short"); }
        var cat = await _db.AttachmentCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (cat == null) { return null; }
        if (cat.IsSystem)
        {
            throw new InvalidOperationException("System category cannot be renamed"); }
        var exists = await _db.AttachmentCategories.AsNoTracking().AnyAsync(c => c.OwnerUserId == ownerUserId && c.Name == name && c.Id != id, ct);
        if (exists)
        {
            throw new ArgumentException("Category name already exists");
        }
        cat.Rename(name);
        await _db.SaveChangesAsync(ct);
        var inUse = await _db.Attachments.AsNoTracking().AnyAsync(a => a.OwnerUserId == ownerUserId && a.CategoryId == id, ct);
        return new AttachmentCategoryDto(cat.Id, cat.Name, cat.IsSystem, inUse);
    }
}
