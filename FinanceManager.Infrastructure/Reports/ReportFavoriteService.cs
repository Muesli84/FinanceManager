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

    private static ReportFavoriteFiltersDto? ToDtoFilters(ReportFavorite e)
    {
        var (acc, con, sp, sec, ccat, scat, secat, secTypes, includeDiv) = e.GetFilters();
        if (acc == null && con == null && sp == null && sec == null && ccat == null && scat == null && secat == null && secTypes == null && includeDiv != true)
        {
            return null;
        }
        return new ReportFavoriteFiltersDto(acc, con, sp, sec, ccat, scat, secat, secTypes, includeDiv);
    }

    private static void ApplyFilters(ReportFavorite e, ReportFavoriteFiltersDto? f)
    {
        if (f == null)
        {
            e.SetFilters(null, null, null, null, null, null, null, null, null);
            return;
        }
        e.SetFilters(f.AccountIds, f.ContactIds, f.SavingsPlanIds, f.SecurityIds, f.ContactCategoryIds, f.SavingsPlanCategoryIds, f.SecurityCategoryIds, f.SecuritySubTypes, f.IncludeDividendRelated);
    }

    public async Task<IReadOnlyList<ReportFavoriteDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
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
                r.Take,
                r.ComparePrevious,
                r.CompareYear,
                r.ShowChart,
                r.Expandable,
                r.CreatedUtc,
                r.ModifiedUtc,
                r.PostingKindsCsv,
                r.AccountIdsCsv,
                r.ContactIdsCsv,
                r.SavingsPlanIdsCsv,
                r.SecurityIdsCsv,
                r.ContactCategoryIdsCsv,
                r.SavingsPlanCategoryIdsCsv,
                r.SecurityCategoryIdsCsv,
                r.SecuritySubTypesCsv,
                r.IncludeDividendRelated,
                r.UseValutaDate
            })
            .ToListAsync(ct);

        return raw.Select(r =>
        {
            var entity = new ReportFavorite(ownerUserId, r.Name, r.PostingKind, r.IncludeCategory, r.Interval, r.ComparePrevious, r.CompareYear, r.ShowChart, r.Expandable, r.Take);
            if (!string.IsNullOrWhiteSpace(r.PostingKindsCsv)) { entity.SetPostingKinds(ParseKinds(r.PostingKindsCsv, r.PostingKind)); }
            entity.SetFilters(ParseCsv(r.AccountIdsCsv), ParseCsv(r.ContactIdsCsv), ParseCsv(r.SavingsPlanIdsCsv), ParseCsv(r.SecurityIdsCsv), ParseCsv(r.ContactCategoryIdsCsv), ParseCsv(r.SavingsPlanCategoryIdsCsv), ParseCsv(r.SecurityCategoryIdsCsv), ParseCsvInt(r.SecuritySubTypesCsv), r.IncludeDividendRelated);
            // apply persisted UseValutaDate onto entity state for DTO creation
            if (r.UseValutaDate) { entity.Update(entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.Take, r.UseValutaDate); }
            return new ReportFavoriteDto(
                r.Id,
                r.Name,
                r.PostingKind,
                r.IncludeCategory,
                r.Interval,
                entity.Take,
                r.ComparePrevious,
                r.CompareYear,
                r.ShowChart,
                r.Expandable,
                r.CreatedUtc,
                r.ModifiedUtc,
                ParseKinds(r.PostingKindsCsv, r.PostingKind),
                ToDtoFilters(entity)
                , r.UseValutaDate
            );
        }).ToList();
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
                r.Take,
                r.ComparePrevious,
                r.CompareYear,
                r.ShowChart,
                r.Expandable,
                r.CreatedUtc,
                r.ModifiedUtc,
                r.PostingKindsCsv,
                r.AccountIdsCsv,
                r.ContactIdsCsv,
                r.SavingsPlanIdsCsv,
                r.SecurityIdsCsv,
                r.ContactCategoryIdsCsv,
                r.SavingsPlanCategoryIdsCsv,
                r.SecurityCategoryIdsCsv,
                r.SecuritySubTypesCsv,
                r.IncludeDividendRelated,
                r.UseValutaDate
            })
            .FirstOrDefaultAsync(ct);
        if (r == null) { return null; }
        var entity = new ReportFavorite(ownerUserId, r.Name, r.PostingKind, r.IncludeCategory, r.Interval, r.ComparePrevious, r.CompareYear, r.ShowChart, r.Expandable, r.Take);
        if (!string.IsNullOrWhiteSpace(r.PostingKindsCsv)) { entity.SetPostingKinds(ParseKinds(r.PostingKindsCsv, r.PostingKind)); }
        entity.SetFilters(ParseCsv(r.AccountIdsCsv), ParseCsv(r.ContactIdsCsv), ParseCsv(r.SavingsPlanIdsCsv), ParseCsv(r.SecurityIdsCsv), ParseCsv(r.ContactCategoryIdsCsv), ParseCsv(r.SavingsPlanCategoryIdsCsv), ParseCsv(r.SecurityCategoryIdsCsv), ParseCsvInt(r.SecuritySubTypesCsv), r.IncludeDividendRelated);
        if (r.UseValutaDate) { entity.Update(entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.Take, r.UseValutaDate); }
        return new ReportFavoriteDto(
            r.Id,
            r.Name,
            r.PostingKind,
            r.IncludeCategory,
            r.Interval,
            entity.Take,
            r.ComparePrevious,
            r.CompareYear,
            r.ShowChart,
            r.Expandable,
            r.CreatedUtc,
            r.ModifiedUtc,
            ParseKinds(r.PostingKindsCsv, r.PostingKind),
            ToDtoFilters(entity)
            , r.UseValutaDate
        );
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

        var entity = new ReportFavorite(ownerUserId, name, request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable, request.Take);
        if (request.PostingKinds is { Count: > 0 })
        {
            entity.SetPostingKinds(request.PostingKinds);
        }
        ApplyFilters(entity, request.Filters);
        // persist UseValutaDate on entity state and touch
        entity.Update(entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.Take, request.UseValutaDate);
        _db.ReportFavorites.Add(entity);
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.Take, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc, EffectiveKinds(entity), ToDtoFilters(entity), entity.UseValutaDate);
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
        entity.Update(request.PostingKind, request.IncludeCategory, request.Interval, request.ComparePrevious, request.CompareYear, request.ShowChart, request.Expandable, request.Take, request.UseValutaDate);
        if (request.PostingKinds is { Count: > 0 })
        {
            entity.SetPostingKinds(request.PostingKinds);
        }
        else
        {
            entity.SetPostingKinds(new[] { request.PostingKind });
        }
        ApplyFilters(entity, request.Filters);
        await _db.SaveChangesAsync(ct);
        return new ReportFavoriteDto(entity.Id, entity.Name, entity.PostingKind, entity.IncludeCategory, entity.Interval, entity.Take, entity.ComparePrevious, entity.CompareYear, entity.ShowChart, entity.Expandable, entity.CreatedUtc, entity.ModifiedUtc, EffectiveKinds(entity), ToDtoFilters(entity), entity.UseValutaDate);
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

    private static IReadOnlyCollection<Guid>? ParseCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Guid.Parse).ToArray();

    private static IReadOnlyCollection<int>? ParseCsvInt(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? null : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
}
