using FinanceManager.Application.Securities;
using FinanceManager.Domain.Securities;
using FinanceManager.Shared.Dtos;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Domain.Attachments; // added

namespace FinanceManager.Infrastructure.Securities;

public sealed class SecurityService : ISecurityService
{
    private readonly AppDbContext _db;
    public SecurityService(AppDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SecurityDto>> ListAsync(Guid ownerUserId, bool onlyActive, CancellationToken ct)
    {
        var q = _db.Securities.AsNoTracking().Where(s => s.OwnerUserId == ownerUserId);
        if (onlyActive) { q = q.Where(s => s.IsActive); }

        return await q
            .OrderBy(s => s.Name)
            .Select(s => new SecurityDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Identifier = s.Identifier,
                AlphaVantageCode = s.AlphaVantageCode,
                CurrencyCode = s.CurrencyCode,
                CategoryId = s.CategoryId,
                CategoryName = s.CategoryId == null
                    ? null
                    : _db.SecurityCategories.Where(c => c.Id == s.CategoryId).Select(c => c.Name).FirstOrDefault(),
                IsActive = s.IsActive,
                CreatedUtc = s.CreatedUtc,
                ArchivedUtc = s.ArchivedUtc
            })
            .ToListAsync(ct);
    }

    public async Task<SecurityDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.Securities.AsNoTracking()
            .Where(s => s.Id == id && s.OwnerUserId == ownerUserId)
            .Select(s => new SecurityDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Identifier = s.Identifier,
                AlphaVantageCode = s.AlphaVantageCode,
                CurrencyCode = s.CurrencyCode,
                CategoryId = s.CategoryId,
                CategoryName = s.CategoryId == null
                    ? null
                    : _db.SecurityCategories.Where(c => c.Id == s.CategoryId).Select(c => c.Name).FirstOrDefault(),
                IsActive = s.IsActive,
                CreatedUtc = s.CreatedUtc,
                ArchivedUtc = s.ArchivedUtc
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SecurityDto> CreateAsync(Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, CancellationToken ct)
    {
        if (categoryId != null)
        {
            bool catExists = await _db.SecurityCategories.AnyAsync(c => c.Id == categoryId && c.OwnerUserId == ownerUserId, ct);
            if (!catExists) { throw new ArgumentException("Invalid category", nameof(categoryId)); }
        }
        bool exists = await _db.Securities.AnyAsync(s => s.OwnerUserId == ownerUserId && s.Name == name, ct);
        if (exists) { throw new ArgumentException("Security name must be unique per user", nameof(name)); }

        var entity = new FinanceManager.Domain.Securities.Security(ownerUserId, name, identifier, description, alphaVantageCode, currencyCode, categoryId);
        _db.Securities.Add(entity);
        await _db.SaveChangesAsync(ct);
        var catName = categoryId == null
            ? null
            : await _db.SecurityCategories.Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        return new SecurityDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Identifier = entity.Identifier,
            AlphaVantageCode = entity.AlphaVantageCode,
            CurrencyCode = entity.CurrencyCode,
            CategoryId = entity.CategoryId,
            CategoryName = catName,
            IsActive = entity.IsActive,
            CreatedUtc = entity.CreatedUtc,
            ArchivedUtc = entity.ArchivedUtc
        };
    }

    public async Task<SecurityDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, CancellationToken ct)
    {
        var entity = await _db.Securities.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return null; }

        if (!string.Equals(entity.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            var exists = await _db.Securities.AnyAsync(s => s.OwnerUserId == ownerUserId && s.Name == name && s.Id != id, ct);
            if (exists) { throw new ArgumentException("Security name must be unique per user", nameof(name)); }
        }
        if (categoryId != null)
        {
            bool catExists = await _db.SecurityCategories.AnyAsync(c => c.Id == categoryId && c.OwnerUserId == ownerUserId, ct);
            if (!catExists) { throw new ArgumentException("Invalid category", nameof(categoryId)); }
        }

        entity.Update(name, identifier, description, alphaVantageCode, currencyCode, categoryId);
        await _db.SaveChangesAsync(ct);

        string? catName = null;
        if (entity.CategoryId != null)
        {
            catName = await _db.SecurityCategories.Where(c => c.Id == entity.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        }

        return new SecurityDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            Identifier = entity.Identifier,
            AlphaVantageCode = entity.AlphaVantageCode,
            CurrencyCode = entity.CurrencyCode,
            CategoryId = entity.CategoryId,
            CategoryName = catName,
            IsActive = entity.IsActive,
            CreatedUtc = entity.CreatedUtc,
            ArchivedUtc = entity.ArchivedUtc
        };
    }

    public async Task<bool> ArchiveAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.Securities.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
        if (entity == null || !entity.IsActive) { return false; }
        entity.Archive();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.Securities.FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return false; }
        if (entity.IsActive) { return false; }

        // Delete attachments for this security
        await _db.Attachments
            .Where(a => a.OwnerUserId == ownerUserId && a.EntityKind == AttachmentEntityKind.Security && a.EntityId == entity.Id)
            .ExecuteDeleteAsync(ct);

        _db.Securities.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
