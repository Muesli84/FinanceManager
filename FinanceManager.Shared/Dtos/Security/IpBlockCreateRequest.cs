using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Security;

/// <summary>
/// Request payload to create an IP block entry.
/// </summary>
public sealed record IpBlockCreateRequest(
    [Required, MaxLength(64)] string IpAddress,
    [MaxLength(200)] string? Reason,
    bool IsBlocked = true);
