using FinanceManager.Application.Notifications;
using FinanceManager.Application.Security;
using FinanceManager.Domain.Notifications;
using FinanceManager.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Security;

/// <summary>
/// Service for managing IP block records used for security and rate-limiting purposes.
/// Provides operations to list, create, update, block/unblock, reset counters and register failed attempts for unknown users.
/// </summary>
public sealed class IpBlockService : IIpBlockService
{
    private readonly AppDbContext _db;
    private readonly ILogger<IpBlockService> _logger;
    private readonly INotificationWriter _notifications;

    /// <summary>
    /// Number of failed unknown-user attempts after which the IP is automatically blocked.
    /// </summary>
    private const int ThresholdAttempts = 3;

    /// <summary>
    /// Time window for counting repeated unknown-user failures.
    /// </summary>
    /// <returns>The result.</returns>
    private static readonly TimeSpan ResetWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="IpBlockService"/> class.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="logger">Logger for diagnostic and audit messages.</param>
    /// <param name="notifications">Notification writer used to inform administrators about security events.</param>
    public IpBlockService(AppDbContext db, ILogger<IpBlockService> logger, INotificationWriter notifications)
    {
        _db = db; _logger = logger; _notifications = notifications;
    }

    /// <summary>
    /// Returns a list of IP block records, optionally filtered to only blocked entries.
    /// </summary>
    /// <param name="onlyBlocked">When <c>true</c> only returns currently blocked IPs; when <c>false</c> returns all records; when <c>null</c> returns all records.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of <see cref="IpBlockDto"/> instances matching the query.</returns>
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

    /// <summary>
    /// Creates a new IP block record. The record can optionally be created already blocked.
    /// </summary>
    /// <param name="ipAddress">The IP address to record.</param>
    /// <param name="reason">Optional reason text when the IP is blocked.</param>
    /// <param name="isBlocked">Flag indicating whether the IP should be created in blocked state.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created <see cref="IpBlockDto"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ipAddress"/> is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an entry for the given IP already exists.</exception>
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

    /// <summary>
    /// Updates an existing IP block record's blocked state or reason.
    /// </summary>
    /// <param name="id">Identifier of the IP block record to update.</param>
    /// <param name="reason">Optional new reason to set when blocking; when <c>null</c> the reason is not changed (unless isBlocked explicitly true).</param>
    /// <param name="isBlocked">Optional desired blocked state; when <c>null</c> only reason may be updated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="IpBlockDto"/>, or <c>null</c> when the record was not found.</returns>
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

    /// <summary>
    /// Blocks the IP block record identified by <paramref name="id"/> if not already blocked.
    /// </summary>
    /// <param name="id">Identifier of the IP block record to block.</param>
    /// <param name="reason">Optional reason to associate with the block.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the record existed (and was blocked if it wasn't already); otherwise <c>false</c> when not found.</returns>
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

    /// <summary>
    /// Unblocks the IP block record identified by <paramref name="id"/> if it exists.
    /// </summary>
    /// <param name="id">Identifier of the IP block record to unblock.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the record existed; otherwise <c>false</c> when not found.</returns>
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

    /// <summary>
    /// Resets the unknown-user failure counters for the specified IP block record.
    /// </summary>
    /// <param name="id">Identifier of the IP block record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the record existed and counters were reset; otherwise <c>false</c>.</returns>
    public async Task<bool> ResetCountersAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        entity.ResetUnknownUserCounters();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Deletes the IP block record permanently.
    /// </summary>
    /// <param name="id">Identifier of the IP block record to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the record existed and was removed; otherwise <c>false</c>.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var entity = await _db.IpBlocks.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (entity == null) return false;
        _db.IpBlocks.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Registers a failed authentication attempt originating from an IP for an unknown user. The method will increment
    /// the failure counter and automatically block the IP when a configured threshold within a sliding window is reached.
    /// </summary>
    /// <param name="ipAddress">The IP address where the failed attempt originated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the failure has been recorded and actions (block/notify) performed if required.</returns>
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

    /// <summary>
    /// Blocks the provided IP address directly and notifies administrators.
    /// </summary>
    /// <param name="ipAddress">The IP address to block.</param>
    /// <param name="reason">Optional reason for blocking.</param>
    /// <param name="ct">Cancellation token.</param>
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

    /// <summary>
    /// Sends a system notification to administrators informing them about the blocked IP address.
    /// </summary>
    /// <param name="ipAddress">The blocked IP address.</param>
    /// <param name="reason">Optional reason text to include in the notification.</param>
    /// <param name="ct">Cancellation token.</param>
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
