using FinanceManager.Domain.Savings;
using FinanceManager.Infrastructure;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure;

public sealed class SavingsPlanCategoryService : ISavingsPlanCategoryService
{
    private readonly AppDbContext _db;
    public SavingsPlanCategoryService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SavingsPlanCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
        => await _db.SavingsPlanCategories
            .Where(c => c.OwnerUserId == ownerUserId)
            .Select(c => new SavingsPlanCategoryDto { Id = c.Id, Name = c.Name, SymbolAttachmentId = c.SymbolAttachmentId })
            .ToListAsync(ct);

    public async Task<SavingsPlanCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
        => await _db.SavingsPlanCategories
            .Where(c => c.Id == id && c.OwnerUserId == ownerUserId)
            .Select(c => new SavingsPlanCategoryDto { Id = c.Id, Name = c.Name, SymbolAttachmentId = c.SymbolAttachmentId })
            .FirstOrDefaultAsync(ct);

    public async Task<SavingsPlanCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct)
    {
        var category = new SavingsPlanCategory(ownerUserId, name);
        _db.SavingsPlanCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanCategoryDto { Id = category.Id, Name = category.Name, SymbolAttachmentId = category.SymbolAttachmentId };
    }

    public async Task<SavingsPlanCategoryDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct)
    {
        var category = await _db.SavingsPlanCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (category == null) return null;
        category.Rename(name);
        await _db.SaveChangesAsync(ct);
        return new SavingsPlanCategoryDto { Id = category.Id, Name = category.Name, SymbolAttachmentId = category.SymbolAttachmentId };
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var category = await _db.SavingsPlanCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (category == null) return false;
        _db.SavingsPlanCategories.Remove(category);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct)
    {
        var category = await _db.SavingsPlanCategories.FirstOrDefaultAsync(c => c.Id == id && c.OwnerUserId == ownerUserId, ct);
        if (category == null) throw new ArgumentException("Category not found", nameof(id));
        category.SetSymbolAttachment(attachmentId);
        await _db.SaveChangesAsync(ct);
    }
}