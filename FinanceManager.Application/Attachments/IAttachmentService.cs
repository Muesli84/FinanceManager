using FinanceManager.Domain.Attachments;

namespace FinanceManager.Application.Attachments;

/// <summary>
/// Service for managing attachments (upload, list, download, update, reassign and delete) for a user.
/// </summary>
public interface IAttachmentService
{
    /// <summary>
    /// Uploads a file attachment for the specified entity.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="kind">Kind of entity the attachment belongs to.</param>
    /// <param name="entityId">Entity identifier.</param>
    /// <param name="content">Stream with file content.</param>
    /// <param name="fileName">Original file name.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="categoryId">Optional category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created <see cref="AttachmentDto"/>.</returns>
    Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, CancellationToken ct);

    /// <summary>
    /// Uploads a file attachment with an explicit role.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="content">The content.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="contentType">The content type.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="role">The role.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>Same as <see cref="UploadAsync(Guid,AttachmentEntityKind,Guid,Stream,string,string,Guid?,CancellationToken)"/> but with a role parameter.</remarks>
    /// <returns>The result.</returns>
    Task<AttachmentDto> UploadAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Stream content, string fileName, string contentType, Guid? categoryId, AttachmentRole role, CancellationToken ct);

    /// <summary>
    /// Creates a URL attachment for an entity.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="url">The url.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AttachmentDto> CreateUrlAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string url, string? fileName, Guid? categoryId, CancellationToken ct);

    /// <summary>
    /// Lists attachments for an entity with paging and optional filters.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="skip">The skip.</param>
    /// <param name="take">The take.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="isUrl">The is url.</param>
    /// <param name="q">The q.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<AttachmentDto>> ListAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, int skip, int take, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct);

    /// <summary>
    /// Counts attachments for an entity matching optional filters.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="isUrl">The is url.</param>
    /// <param name="q">The q.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<int> CountAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid? categoryId, bool? isUrl, string? q, CancellationToken ct);

    /// <summary>
    /// Downloads an attachment if available to the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple (Content stream, FileName, ContentType) or null when not found.</returns>
    Task<(Stream Content, string FileName, string ContentType)?> DownloadAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);

    /// <summary>
    /// Deletes an attachment owned by the user.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid ownerUserId, Guid attachmentId, CancellationToken ct);

    /// <summary>
    /// Updates the category of an attachment.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> UpdateCategoryAsync(Guid ownerUserId, Guid attachmentId, Guid? categoryId, CancellationToken ct);

    /// <summary>
    /// Updates core properties of an attachment such as filename and category.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="categoryId">The category id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> UpdateCoreAsync(Guid ownerUserId, Guid attachmentId, string? fileName, Guid? categoryId, CancellationToken ct);

    /// <summary>
    /// Reassigns all attachments from one entity to another.
    /// </summary>
    /// <param name="fromKind">The from kind.</param>
    /// <param name="fromId">The from id.</param>
    /// <param name="toKind">The to kind.</param>
    /// <param name="toId">The to id.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReassignAsync(AttachmentEntityKind fromKind, Guid fromId, AttachmentEntityKind toKind, Guid toId, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Creates a new referencing attachment entry pointing to an existing master attachment.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="masterAttachmentId">The master attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AttachmentDto> CreateReferenceAsync(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, Guid masterAttachmentId, CancellationToken ct);
}
