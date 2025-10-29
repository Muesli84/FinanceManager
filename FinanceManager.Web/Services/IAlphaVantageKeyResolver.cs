using FinanceManager.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinanceManager.Web.Services;

public interface IAlphaVantageKeyResolver
{
    Task<string?> GetForUserAsync(Guid userId, CancellationToken ct);
    Task<string?> GetSharedAsync(CancellationToken ct);
}

public sealed class AlphaVantageKeyResolver : IAlphaVantageKeyResolver
{
    private readonly AppDbContext _db;

    public AlphaVantageKeyResolver(AppDbContext db) { _db = db; }

    public async Task<string?> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        var key = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.AlphaVantageApiKey)
            .SingleOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(key)) return key;

        return await GetSharedAsync(ct);
    }

    public async Task<string?> GetSharedAsync(CancellationToken ct)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.IsAdmin && u.ShareAlphaVantageApiKey && u.AlphaVantageApiKey != null)
            .OrderBy(u => u.UserName) // deterministic choice — use mapped Identity property
            .Select(u => u.AlphaVantageApiKey!)
            .FirstOrDefaultAsync(ct);
    }
}