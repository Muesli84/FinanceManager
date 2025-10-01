using FinanceManager.Domain.Attachments;
using FinanceManager.Shared.Dtos;

namespace FinanceManager.Application.Attachments;

public interface IAttachmentService
{
    Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct);
    Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct);
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, CancellationToken ct);
    Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);
    Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct);
    Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct);
}

public interface IAttachmentCategoryService
{
    Task<IReadOnlyList<AttachmentCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);
    Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);
    Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct);
}
