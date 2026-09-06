namespace FinanceManager.Domain.Attachments;

/// <summary>
/// Attachment roles describing special meaning for attachments (e.g. symbol images).
/// </summary>
public enum AttachmentRole : short
{
    /// <summary>
    /// Regular attachment without special role.
    /// </summary>
    Regular = 0,

    /// <summary>
    /// Symbol attachment, typically used as an icon for an entity.
    /// </summary>
    Symbol = 1
}

/// <summary>
/// Domain entity that represents a binary attachment or URL referenced by an entity.
/// Stores metadata and optional content for small attachments.
/// </summary>
public sealed class Attachment : Entity
{
    /// <summary>
    /// Owner user identifier.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Kind of entity this attachment belongs to.
    /// </summary>
    public AttachmentEntityKind EntityKind { get; private set; }

    /// <summary>
    /// Identifier of the parent entity.
    /// </summary>
    public Guid EntityId { get; private set; }

    /// <summary>
    /// Original file name (or URL description for URL attachments).
    /// </summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// MIME content type for file attachments.
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>
    /// Size in bytes for file attachments.
    /// </summary>
    public long SizeBytes { get; private set; }

    /// <summary>
    /// SHA-256 hex digest of the content when available.
    /// </summary>
    public string? Sha256 { get; private set; }

    /// <summary>
    /// Optional category id associated with the attachment.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>
    /// Upload timestamp in UTC.
    /// </summary>
    public DateTime UploadedUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional binary content stored inline (may be null for large files).
    /// </summary>
    public byte[]? Content { get; private set; }

    /// <summary>
    /// Optional URL for URL attachments.
    /// </summary>
    public string? Url { get; private set; }

    /// <summary>
    /// Reference to another attachment (e.g. thumbnail or derived resource).
    /// </summary>
    public Guid? ReferenceAttachmentId { get; private set; }

    /// <summary>
    /// Optional human note attached to the attachment entry.
    /// </summary>
    public string? Note { get; private set; }

    /// <summary>
    /// Attachment role indicating special meaning (default: Regular).
    /// </summary>
    public AttachmentRole Role { get; private set; } = AttachmentRole.Regular;

    /// <summary>
    /// Constructs a new attachment entity.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="entityKind">Kind of parent entity.</param>
    /// <param name="entityId">Parent entity identifier.</param>
    /// <param name="fileName">Original file name or URL description.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="sizeBytes">Size in bytes of the attachment.</param>
    /// <param name="sha256">Optional SHA-256 hex digest.</param>
    /// <param name="categoryId">Optional category identifier.</param>
    /// <param name="content">Optional inline content bytes.</param>
    /// <param name="url">Optional URL for URL attachments.</param>
    /// <param name="referenceAttachmentId">Optional reference to another attachment.</param>
    /// <param name="role">Attachment role (e.g. symbol).</param>
    public Attachment(
        Guid ownerUserId,
        AttachmentEntityKind entityKind,
        Guid entityId,
        string fileName,
        string contentType,
        long sizeBytes,
        string? sha256,
        Guid? categoryId,
        byte[]? content,
        string? url,
        Guid? referenceAttachmentId = null,
        AttachmentRole role = AttachmentRole.Regular)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        EntityKind = entityKind;
        EntityId = entityId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        CategoryId = categoryId;
        Content = content;
        Url = url;
        ReferenceAttachmentId = referenceAttachmentId;
        Role = role;
        if (Content is null && string.IsNullOrWhiteSpace(Url) && ReferenceAttachmentId == null)
        {
            throw new ArgumentException("Either content, URL, or reference must be provided for an attachment.");
        }
    }

    /// <summary>
    /// Sets or clears the category id for the attachment.
    /// </summary>
    /// <param name="categoryId">Category id to set or null to clear.</param>
    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;

    /// <summary>
    /// Sets a human readable note for the attachment.
    /// </summary>
    /// <param name="note">Note text or null to clear.</param>
    public void SetNote(string? note)
        => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    /// <summary>
    /// Sets a reference to another attachment.
    /// </summary>
    /// <param name="referenceId">Reference attachment id or null to clear.</param>
    public void SetReference(Guid? referenceId)
    {
        ReferenceAttachmentId = referenceId;
    }

    /// <summary>
    /// Sets the role for the attachment.
    /// </summary>
    /// <param name="role">New attachment role.</param>
    public void SetRole(AttachmentRole role) => Role = role;

    /// <summary>
    /// Renames the attachment file name (trimmed) and updates the uploaded timestamp.
    /// </summary>
    /// <param name="fileName">New file name.</param>
    public void Rename(string fileName)
    {
        FileName = fileName?.Trim() ?? string.Empty;
        UploadedUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Reassigns this attachment to a different parent entity.
    /// </summary>
    /// <param name="toKind">Target entity kind.</param>
    /// <param name="toEntityId">Target entity identifier.</param>
    public void Reassign(AttachmentEntityKind toKind, Guid toEntityId)
    {
        EntityKind = toKind;
        EntityId = toEntityId;
    }

    /// <summary>
    /// Backup DTO - include Content bytes only if present and small; keep it for restore completeness.
    /// </summary>
    /// <param name="Id">Identifier of the entity.</param>
    /// <param name="CreatedUtc">The created utc.</param>
    /// <param name="ModifiedUtc">The modified utc.</param>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="EntityKind">The entity kind.</param>
    /// <param name="EntityId">The entity id.</param>
    /// <param name="FileName">The file name.</param>
    /// <param name="ContentType">The content type.</param>
    /// <param name="SizeBytes">The size bytes.</param>
    /// <param name="Sha256">The sha256.</param>
    /// <param name="CategoryId">The category id.</param>
    /// <param name="UploadedUtc">The uploaded utc.</param>
    /// <param name="Content">The content.</param>
    /// <param name="Url">The url.</param>
    /// <param name="ReferenceAttachmentId">The reference attachment id.</param>
    /// <param name="Note">The note.</param>
    /// <param name="Role">The role.</param>
    /// <returns>The result.</returns>
    public sealed record AttachmentBackupDto(Guid Id, DateTime CreatedUtc, DateTime? ModifiedUtc, Guid OwnerUserId, AttachmentEntityKind EntityKind, Guid EntityId, string FileName, string ContentType, long SizeBytes, string? Sha256, Guid? CategoryId, DateTime UploadedUtc, byte[]? Content, string? Url, Guid? ReferenceAttachmentId, string? Note, AttachmentRole Role);

    /// <summary>
    /// Creates a backup DTO for this Attachment. Content bytes are included if available.
    /// </summary>
    /// <returns>The result.</returns>
    public AttachmentBackupDto ToBackupDto()
        => new AttachmentBackupDto(Id, CreatedUtc, ModifiedUtc, OwnerUserId, EntityKind, EntityId, FileName, ContentType, SizeBytes, Sha256, CategoryId, UploadedUtc, Content, Url, ReferenceAttachmentId, Note, Role);

    /// <summary>
    /// Assigns values from a backup DTO to this entity.
    /// </summary>
    /// <param name="dto">The dto.</param>
    public void AssignBackupDto(AttachmentBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        EntityKind = dto.EntityKind;
        EntityId = dto.EntityId;
        FileName = dto.FileName;
        ContentType = dto.ContentType;
        SizeBytes = dto.SizeBytes;
        Sha256 = dto.Sha256;
        CategoryId = dto.CategoryId;
        UploadedUtc = dto.UploadedUtc;
        Content = dto.Content;
        Url = dto.Url;
        ReferenceAttachmentId = dto.ReferenceAttachmentId;
        Note = dto.Note;
        Role = dto.Role;
        SetDates(dto.CreatedUtc, dto.ModifiedUtc);
    }
}
