namespace FinanceManager.Application.Security;

public interface IIpBlockService
{
    Task<IReadOnlyList<IpBlockDto>> ListAsync(bool? onlyBlocked, CancellationToken ct);
    Task<IpBlockDto> CreateAsync(string ipAddress, string? reason, bool isBlocked, CancellationToken ct);
    Task<IpBlockDto?> UpdateAsync(Guid id, string? reason, bool? isBlocked, CancellationToken ct);
    Task<bool> BlockAsync(Guid id, string? reason, CancellationToken ct);
    Task<bool> UnblockAsync(Guid id, CancellationToken ct);
    Task<bool> ResetCountersAsync(Guid id, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct);
    Task RegisterUnknownUserFailureAsync(string ipAddress, CancellationToken ct);
    Task BlockByAddressAsync(string ipAddress, string? reason, CancellationToken ct);
}

