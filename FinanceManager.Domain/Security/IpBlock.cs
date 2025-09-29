namespace FinanceManager.Domain.Security;

public sealed class IpBlock : Entity, IAggregateRoot
{
    private IpBlock() { }

    public IpBlock(string ipAddress)
    {
        IpAddress = Guards.NotNullOrWhiteSpace(ipAddress, nameof(ipAddress)).Trim();
        IsBlocked = false;
    }

    public string IpAddress { get; private set; } = null!;

    // Whether this IP is currently blocked (blacklisted)
    public bool IsBlocked { get; private set; }
    public DateTime? BlockedAtUtc { get; private set; }
    public string? BlockReason { get; private set; }

    // Global counter for attempts with unknown/non-existing username from this IP
    public int UnknownUserFailedAttempts { get; private set; }
    public DateTime? UnknownUserLastFailedUtc { get; private set; }

    public void Rename(string ipAddress)
    {
        IpAddress = Guards.NotNullOrWhiteSpace(ipAddress, nameof(ipAddress)).Trim();
        Touch();
    }

    public int RegisterUnknownUserFailure(DateTime utcNow, TimeSpan resetAfter)
    {
        if (UnknownUserLastFailedUtc.HasValue && utcNow - UnknownUserLastFailedUtc.Value >= resetAfter)
        {
            UnknownUserFailedAttempts = 0;
        }
        UnknownUserFailedAttempts++;
        UnknownUserLastFailedUtc = utcNow;
        Touch();
        return UnknownUserFailedAttempts;
    }

    public void ResetUnknownUserCounters()
    {
        UnknownUserFailedAttempts = 0;
        UnknownUserLastFailedUtc = null;
        Touch();
    }

    public void Block(DateTime utcNow, string? reason = null)
    {
        IsBlocked = true;
        BlockedAtUtc = utcNow;
        BlockReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();
    }

    public void Unblock()
    {
        IsBlocked = false;
        BlockedAtUtc = null;
        BlockReason = null;
        Touch();
    }
}
