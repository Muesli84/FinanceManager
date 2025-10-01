using System;

namespace FinanceManager.Domain.Attachments;

public sealed class Attachment
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; private set; }
    public AttachmentEntityKind EntityKind { get; private set; }
    public Guid EntityId { get; private set; }

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public Guid? CategoryId { get; private set; }
    public DateTime UploadedUtc { get; private set; } = DateTime.UtcNow;

    // Store either BLOB or external URL (one of both required)
    public byte[]? Content { get; private set; }
    public string? Url { get; private set; }

    public string? Note { get; private set; }

    private Attachment() { }

    public Attachment(Guid ownerUserId, AttachmentEntityKind kind, Guid entityId, string fileName, string contentType, long sizeBytes, string? sha256, Guid? categoryId, byte[]? content, string? url)
    {
        OwnerUserId = ownerUserId;
        EntityKind = kind;
        EntityId = entityId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        CategoryId = categoryId;
        Content = content;
        Url = url;
        if (Content is null && string.IsNullOrWhiteSpace(Url))
        {
            throw new ArgumentException("Either content or URL must be provided for an attachment.");
        }
    }

    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;
    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public void Reassign(AttachmentEntityKind toKind, Guid toEntityId)
    {
        EntityKind = toKind;
        EntityId = toEntityId;
    }
}
