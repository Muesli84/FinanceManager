using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record IpBlockCreateRequest(
    [Required, MaxLength(64)] string IpAddress,
    [MaxLength(200)] string? Reason,
    bool IsBlocked = true);

public sealed record IpBlockUpdateRequest(
    [MaxLength(200)] string? Reason,
    bool? IsBlocked);
