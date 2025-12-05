namespace FinanceManager.Shared.Dtos.Security;

/// <summary>
/// DTO representing an IP block entry including status and counters.
/// </summary>
/// <param name="Id">Unique identifier of the IP block entry.</param>
/// <param name="IpAddress">IPv4/IPv6 address string.</param>
/// <param name="IsBlocked">True when the address is currently blocked.</param>
/// <param name="BlockedAtUtc">UTC timestamp when the IP was blocked.</param>
/// <param name="BlockReason">Optional reason why the IP was blocked.</param>
/// <param name="UnknownUserFailedAttempts">Failed attempts for unknown users since last reset.</param>
/// <param name="UnknownUserLastFailedUtc">UTC timestamp of the last failed unknown user attempt.</param>
/// <param name="CreatedUtc">UTC timestamp when the entry was created.</param>
/// <param name="ModifiedUtc">UTC timestamp when the entry was last modified (if any).</param>
public sealed record IpBlockDto(
    Guid Id,
    string IpAddress,
    bool IsBlocked,
    DateTime? BlockedAtUtc,
    string? BlockReason,
    int UnknownUserFailedAttempts,
    DateTime? UnknownUserLastFailedUtc,
    DateTime CreatedUtc,
    DateTime? ModifiedUtc);
