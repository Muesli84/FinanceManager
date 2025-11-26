using FinanceManager.Application.Reports;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Infrastructure.Reports;

public sealed class HomeKpiService : IHomeKpiService
{
    private readonly AppDbContext _db;
    public HomeKpiService(AppDbContext db) => _db = db;

    private static HomeKpiDto Map(FinanceManager.Domain.Reports.HomeKpi e, string? favName)
        => new(
            e.Id,
            e.Kind,
            e.ReportFavoriteId,
            favName,
            e.Title,
            e.PredefinedType,
            e.DisplayMode,
            e.SortOrder,
            e.CreatedUtc,
            e.ModifiedUtc);

    public async Task<IReadOnlyList<HomeKpiDto>> ListAsync(Guid ownerUserId, CancellationToken ct)
    {
        var data = await _db.HomeKpis.AsNoTracking()
            .Where(k => k.OwnerUserId == ownerUserId)
            .OrderBy(k => k.SortOrder).ThenBy(k => k.CreatedUtc)
            .Select(k => new { Kpi = k, FavName = k.ReportFavoriteId == null ? null : _db.ReportFavorites.Where(f => f.Id == k.ReportFavoriteId).Select(f => f.Name).FirstOrDefault() })
            .ToListAsync(ct);
        return data.Select(x => Map(x.Kpi, x.FavName)).ToList();
    }

    public async Task<HomeKpiDto> CreateAsync(Guid ownerUserId, HomeKpiCreateRequest request, CancellationToken ct)
    {
        if (request.Kind == HomeKpiKind.ReportFavorite && request.ReportFavoriteId == null)
        {
            throw new ArgumentException("ReportFavoriteId required for kind ReportFavorite", nameof(request.ReportFavoriteId));
        }
        if (request.ReportFavoriteId.HasValue)
        {
            var owned = await _db.ReportFavorites.AsNoTracking().AnyAsync(f => f.Id == request.ReportFavoriteId.Value && f.OwnerUserId == ownerUserId, ct);
            if (!owned) { throw new InvalidOperationException("Favorite not found or not owned"); }
        }
        var entity = new FinanceManager.Domain.Reports.HomeKpi(ownerUserId, request.Kind, request.DisplayMode, request.SortOrder, request.ReportFavoriteId);
        // For predefined KPIs, ensure a default when omitted (back-compat): cycle by sort index
        if (request.Kind == HomeKpiKind.Predefined && request.PredefinedType == null)
        {
            var fallback = (HomeKpiPredefined)(request.SortOrder % Enum.GetValues<HomeKpiPredefined>().Length);
            entity.SetPredefined(fallback);
        }
        else
        {
            entity.SetPredefined(request.PredefinedType);
        }
        entity.SetTitle(request.Title);
        _db.HomeKpis.Add(entity);
        await _db.SaveChangesAsync(ct);
        var favName = request.ReportFavoriteId == null ? null : await _db.ReportFavorites.AsNoTracking().Where(f => f.Id == request.ReportFavoriteId).Select(f => f.Name).FirstOrDefaultAsync(ct);
        return Map(entity, favName);
    }

    public async Task<HomeKpiDto?> UpdateAsync(Guid id, Guid ownerUserId, HomeKpiUpdateRequest request, CancellationToken ct)
    {
        var entity = await _db.HomeKpis.FirstOrDefaultAsync(k => k.Id == id && k.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return null; }
        if (request.Kind == HomeKpiKind.ReportFavorite && request.ReportFavoriteId == null)
        {
            throw new ArgumentException("ReportFavoriteId required for kind ReportFavorite", nameof(request.ReportFavoriteId));
        }
        if (request.ReportFavoriteId.HasValue)
        {
            var owned = await _db.ReportFavorites.AsNoTracking().AnyAsync(f => f.Id == request.ReportFavoriteId.Value && f.OwnerUserId == ownerUserId, ct);
            if (!owned) { throw new InvalidOperationException("Favorite not found or not owned"); }
        }
        entity.SetFavorite(request.ReportFavoriteId);
        // Preserve existing predefined type if not supplied
        entity.SetPredefined(request.PredefinedType ?? entity.PredefinedType);
        entity.SetDisplayMode(request.DisplayMode);
        entity.SetSortOrder(request.SortOrder);
        await _db.SaveChangesAsync(ct);
        var favName = request.ReportFavoriteId == null ? null : await _db.ReportFavorites.AsNoTracking().Where(f => f.Id == request.ReportFavoriteId).Select(f => f.Name).FirstOrDefaultAsync(ct);
        return Map(entity, favName);
    }

    public async Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct)
    {
        var entity = await _db.HomeKpis.FirstOrDefaultAsync(k => k.Id == id && k.OwnerUserId == ownerUserId, ct);
        if (entity == null) { return false; }
        _db.HomeKpis.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
