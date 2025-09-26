using FinanceManager.Application.Reports;
using FinanceManager.Domain.Reports;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Reports;

public sealed class ReportFavoriteService : IReportFavoriteService
{
    private readonly AppDbContext _db;
    public ReportFavoriteService(AppDbContext db) => _db = db;

    private static IReadOnlyCollection<int> EffectiveKinds(ReportFavorite entity)
        => entity.GetPostingKinds();

    private static IReadOnlyCollection<int> ParseKinds(string? csv, int fallback)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new[] { fallback };
        }
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<int>(parts.Length);
        foreach (var p in parts)
        {
            if (int.TryParse(p, out var v))
            {
                list.Add(v);
            }
        }
        return list.Count == 0 ? new[] { fallback } : list.Distinct().ToArray();
    }

    public async Task<IReadOnlyList<ReportFavoriteDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        // Fetch raw data first (avoid int.Parse translation in SQL), then map in memory
        var raw = await _db.ReportFavorites.AsNoTracking()
            .Where(r => r.OwnerUserId == ownerUserId)
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.PostingKind,
                r.IncludeCategory,
                r.Interval,
                r.ComparePrevious,
                r.CompareYear,
                r.ShowChart,
                r.Expandable,
                r.CreatedUtc,
                r.ModifiedUtc,
                r.PostingKindsCsv
            })
            .ToListAsync(ct);

        return raw.Select(r => new ReportFavoriteDto(
            r.Id,
            r.Name,
            r.PostingKind,
            r.IncludeCategory,
            r.Interval,
            r.ComparePrevious,
            r.CompareYear,
            r.ShowChart,
            r.Expandable,
            r.CreatedUtc,
            r.ModifiedUtc,
            ParseKinds(r.PostingKindsCsv, r.PostingKind)
        )).ToList();
    }

    public async Task<ReportFavoriteDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var r = await _db.ReportFavorites.AsNoTracking()
            .Where(r => r.Id == id && r.OwnerUserId == ownerUserId)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.PostingKind,
                r.IncludeCategory,
                r.Interval,
                r.ComparePrevious,
                r.CompareYear,
                r.ShowChart,
                r.Expandable,
                r.CreatedUtc,
                r.ModifiedUtc,
                r.PostingKindsCsv
            })
            .FirstOrDefaultAsync(ct);
        if (r == null) { return null; }
        return new ReportFavoriteDto(
            r.Id,
            r.Name,
            r.PostingKind,
            r.IncludeCategory,
            r.Interval,
            r.ComparePrevious,
            r.CompareYear,
            r.ShowChart,
            r.Expandable,
            r.CreatedUtc,
            r.ModifiedUtc,
            ParseKinds(r.PostingKindsCsv, r.PostingKind));
    }

    public async Task<ReportFavoriteDto> CreateAsync(Guid ownerUserId, ReportFavoriteCreateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name required", nameof(request.Name));
        }
        var name = request.Name.Trim();

        var exists = await _db.ReportFavorites.AsNoTracking().AnyAsync(r => r.OwnerUserId == ownerUserId && r.Name == name, ct);
        if (exists)
        {
            throw new InvalidOperationException("Duplicate favorite name");
        }

        var entity = new ReportFavorite(ownerUserId, name, request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable);
        if (request.PostingKinds is { Count: > 0 })
        {
            entity.SetPostingKinds(request.PostingKinds);
        }
        _db.ReportFavorites.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc, EffectiveKinds(entity));
    }

    public async Task<ReportFavoriteDto?> UpdateAsync(Guid id, Guid ownerUserId, ReportFavoriteUpdateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Name required", nameof(request.Name));
        }
        var entity = await _db.ReportFavorites.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return null;
        }

        var name = request.Name.Trim();
        var duplicate = await _db.ReportFavorites.AsNoTracking().AnyAsync(r => r.OwnerUserId == ownerUserId && r.Name == name && r.Id != id, ct);
        if (duplicate)
        {
            throw new InvalidOperationException("Duplicate favorite name");
        }

        entity.Rename(name);
        entity.Update(request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable);
        if (request.PostingKinds is { Count: > 0 })
        {
            entity.SetPostingKinds(request.PostingKinds);
        }
        else
        {
            entity.SetPostingKinds(new[] { request.PostingKind });
        }
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc, EffectiveKinds(entity));
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.ReportFavorites.FirstOrDefaultAsync(r => r.Id == id && r.OwnerUserId == ownerUserId, ct);
        if (entity == null)
        {
            return false;
        }
        _db.ReportFavorites.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
