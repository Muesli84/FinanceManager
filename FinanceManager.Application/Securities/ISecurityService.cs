namespace FinanceManager.Application.Securities;

public interface ISecurityService
{
    Task<IReadOnlyList<SecurityDto>> ListAsync(Guid ownerUserId, bool onlyActive, CancellationToken ct);
    Task<SecurityDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<SecurityDto> CreateAsync(Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, CancellationToken ct);
    Task<SecurityDto?> UpdateAsync(Guid id, Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId, CancellationToken ct);
    Task<bool> ArchiveAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
    Task<int> CountAsync(Guid ownerUserId, bool onlyActive, CancellationToken ct);
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);
}
