using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Security;

/// <summary>
/// Request payload to update an existing IP block entry.
/// </summary>
public sealed record IpBlockUpdateRequest(
    [MaxLength(200)] string? Reason,
    bool? IsBlocked);
