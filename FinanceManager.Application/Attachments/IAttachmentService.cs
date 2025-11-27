using FinanceManager.Domain.Attachments;

namespace FinanceManager.Application.Attachments;

public interface IAttachmentService
{
    // Backwards-compatible upload
    Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct);

    // New overload allowing to mark attachment role (Symbol vs Regular)
    Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, AttachmentRole role, CancellationToken ct);

    Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct);
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, CancellationToken ct);
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct);
    Task<int> CountAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct);
    Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);
    Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);
    Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct);
    Task<bool> UpdateCoreAsync(Guid ownerUserId, Guid attachmentId, string? fileName, Guid? categoryId, CancellationToken ct);
    Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct);
    Task<AttachmentDto> CreateReferenceAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid masterAttachmentId, CancellationToken ct);
}
