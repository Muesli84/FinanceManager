using FinanceManager.Application.Notifications;
using FinanceManager.Application.Security;
using FinanceManager.Domain.Notifications;
using FinanceManager.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Security;

public sealed class IpBlockService : IIpBlockService
{
    private readonly AppDbContext _db;
    private readonly ILogger<IpBlockService> _logger;
    private readonly INotificationWriter _notifications;
    private const int ThresholdAttempts = 3;
    private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(5);

    public IpBlockService(AppDbContext db, ILogger<IpBlockService> logger, INotificationWriter notifications)
    {
        _db = db; _logger = logger; _notifications = notifications;
    }

    public async Task<IReadOnlyList<IpBlockDto>> ListAsync(bool? onlyBlocked, CancellationToken ct)
    {
        var query = _db.IpBlocks.AsNoTracking().AsQueryable();
        if (onlyBlocked == true)
        {
            query = query.Where(b => b.IsBlocked);
        }
        var list = await query
            .OrderByDescending(b => b.IsBlocked)
            .ThenByDescending(b => b.UnknownUserLastFailedUtc)
            .ThenBy(b => b.IpAddress)
            .Select(b => new IpBlockDto(b.Id, b.IpAddress, b.IsBlocked, b.BlockedAtUtc, b.BlockReason, b.UnknownUserFailedAttempts, b.UnknownUserLastFailedUtc, b.CreatedUtc, b.ModifiedUtc))
            .ToListAsync(ct);
        return list;
    }

    public async Task<IpBlockDto> CreateAsync(string ipAddress, string? reason, bool isBlocked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) throw new ArgumentException("ipAddress required", nameof(ipAddress));
        var existing = await _db.IpBlocks.AsNoTracking().AnyAsync(b => b.IpAddress == ipAddress, ct);
        if (existing) throw new InvalidOperationException("IP already exists in block list");
        var entity = new IpBlock(ipAddress);
        if (isBlocked)
        {
            entity.Block(DateTime.UtcNow, reason);
        }
        _db.IpBlocks.Add(entity);
        await _db.SaveChangesAsync(ct);

        if (isBlocked)
        {
            await NotifyAdminsAsync(entity.IpAddress, entity.BlockReason, ct);
        }

        return new IpBlockDto(entity.Id, entity.IpAddress, entity.IsBlocked, entity.BlockedAtUtc, entity.BlockReason, entity.UnknownUserFailedAttempts, entity.UnknownUserLastFailedUtc, entity.CreatedUtc, entity.ModifiedUtc);
    }

    public async Task<IpBlockDto?> UpdateAsync(Guid id, string? reason, bool? isBlocked, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return null;
        var wasBlocked = entity.IsBlocked;
        if (isBlocked.HasValue)
        {
            if (isBlocked.Value && !entity.IsBlocked)
            {
                entity.Block(DateTime.UtcNow, reason);
            }
            else if (!isBlocked.Value && entity.IsBlocked)
            {
                entity.Unblock();
            }
        }
        else if (reason != null && entity.IsBlocked)
        {
            // update reason only
            entity.Block(DateTime.UtcNow, reason);
        }
        await _db.SaveChangesAsync(ct);

        if (!wasBlocked && entity.IsBlocked)
        {
            await NotifyAdminsAsync(entity.IpAddress, entity.BlockReason, ct);
        }

        return new IpBlockDto(entity.Id, entity.IpAddress, entity.IsBlocked, entity.BlockedAtUtc, entity.BlockReason, entity.UnknownUserFailedAttempts, entity.UnknownUserLastFailedUtc, entity.CreatedUtc, entity.ModifiedUtc);
    }

    public async Task<bool> BlockAsync(Guid id, string? reason, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        if (!entity.IsBlocked)
        {
            entity.Block(DateTime.UtcNow, reason);
            await _db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(entity.IpAddress, entity.BlockReason, ct);
        }
        return true;
    }

    public async Task<bool> UnblockAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        if (entity.IsBlocked)
        {
            entity.Unblock();
            await _db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<bool> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        entity.ResetUnknownUserCounters();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        _db.IpBlocks.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RegisterUnknownUserFailureAsync(string ipAddress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) { return; }
        var now = DateTime.UtcNow;
        var block = await _db.IpBlocks.FirstOrDefaultAsync(b => b.IpAddress == ipAddress, ct);
        if (block == null)
        {
            block = new IpBlock(ipAddress);
            _db.IpBlocks.Add(block);
        }
        var count = block.RegisterUnknownUserFailure(now, ResetWindow);
        if (count >= ThresholdAttempts)
        {
            block.Block(now, "Unknown user failures threshold reached");
            await _db.SaveChangesAsync(ct);
            await NotifyAdminsAsync(ipAddress, block.BlockReason, ct);
            return;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task BlockByAddressAsync(string ipAddress, string? reason, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) { return; }
        var now = DateTime.UtcNow;
        var block = await _db.IpBlocks.FirstOrDefaultAsync(b => b.IpAddress == ipAddress, ct);
        if (block == null)
        {
            block = new IpBlock(ipAddress);
            _db.IpBlocks.Add(block);
        }
        block.Block(now, reason);
        await _db.SaveChangesAsync(ct);
        await NotifyAdminsAsync(ipAddress, reason, ct);
    }

    private async Task NotifyAdminsAsync(string ipAddress, string? reason, CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var title = "Security alert: IP blocked";
        var msg = string.IsNullOrWhiteSpace(reason)
            ? $"The IP address {ipAddress} has been blocked."
            : $"The IP address {ipAddress} has been blocked. Reason: {reason}";
        var key = $"setup:ip-blocks?focus={Uri.EscapeDataString(ipAddress)}"; // hint link for UI
        await _notifications.CreateForAdminsAsync(title, msg, NotificationType.SystemAlert, NotificationTarget.HomePage, todayUtc, key, ct);
    }
}
