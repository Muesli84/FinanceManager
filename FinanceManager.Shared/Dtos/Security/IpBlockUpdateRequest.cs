using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Security;

/// <summary>
/// Request payload to update an existing IP block entry.
/// </summary>
/// <param name="Reason">The reason.</param>
/// <param name="IsBlocked">The is blocked.</param>
/// <returns>The result.</returns>
public sealed record IpBlockUpdateRequest(
    [MaxLength(200)] string? Reason,
    bool? IsBlocked);
