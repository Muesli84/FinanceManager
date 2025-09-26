using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Reports;

public sealed class ReportFavoriteService : IReportFavoriteService
{
    private readonly AppDbContext _db;
    public ReportFavoriteService(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<ReportFavoriteDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        return await _db.ReportFavorites.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderBy(r => r.Name)
            .Select(r => new ReportFavoriteDto(r.Id, r.Name, r.PostingKind, r.IncludeCategory, r.Interval, r.ComparePrevious, r.CompareYear, r.ShowChart, r.Expandable, r.CreatedUtc, r.ModifiedUtc))
            .ToListAsync(ct);
    }

    public async Task<ReportFavoriteDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        return await _db.ReportFavorites.AsNoTracking()
            .Where(r => r.Id == id && r.OwnerUserId == ownerUserId)
            .Select(r => new ReportFavoriteDto(r.Id, r.Name, r.PostingKind, r.IncludeCategory, r.Interval, r.ComparePrevious, r.CompareYear, r.ShowChart, r.Expandable, r.CreatedUtc, r.ModifiedUtc))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ReportFavoriteDto> CreateAsync(Guid ownerUserId, ReportFavoriteCreateRequest request, CancellationToken ct)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name required", nameof(request.Name));
        var name = request.Name.Trim();

        var exists = await _db.ReportFavorites.AsNoTracking().AnyAsync(r => r.OwnerUserId == ownerUserId && r.Name == name, ct);
        if (exists) throw new InvalidOperationException("Duplicate favorite name");

        var entity = new ReportFavorite(ownerUserId, name, request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable);
        _db.ReportFavorites.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc);
    }

    public async Task<ReportFavoriteDto?> UpdateAsync(Guid id, Guid ownerUserId, ReportFavoriteUpdateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Name required", nameof(request.Name));
        var entity = await _db.ReportFavorites.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null) return null;

        var name = request.Name.Trim();
        var duplicate = await _db.ReportFavorites.AsNoTracking().AnyAsync(r => r.OwnerUserId == ownerUserId && r.Name == name && r.Id != id, ct);
        if (duplicate) throw new InvalidOperationException("Duplicate favorite name");

        entity.Rename(name);
        entity.Update(request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable);
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.ReportFavorites.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null) return false;
        _db.ReportFavorites.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
