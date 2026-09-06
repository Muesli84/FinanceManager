using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Security;

/// <summary>
/// Request payload to create an IP block entry.
/// </summary>
/// <param name="IpAddress">The ip address.</param>
/// <param name="Reason">The reason.</param>
/// <param name="IsBlocked">The is blocked.</param>
/// <returns>The result.</returns>
public sealed record IpBlockCreateRequest(
    [Required, MaxLength(64)] string IpAddress,
    [MaxLength(200)] string? Reason,
    bool IsBlocked = true);
