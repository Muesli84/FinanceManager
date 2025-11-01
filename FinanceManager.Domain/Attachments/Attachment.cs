using System;
using System.IO;

namespace FinanceManager.Domain.Attachments;

public enum AttachmentRole : short
{
    Regular = 0,
    Symbol = 1
}

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

    // Store either BLOB or external URL (one of both required),
    // or reference another attachment (for deduplication across postings)
    public byte[]? Content { get; private set; }
    public string? Url { get; private set; }
    public Guid? ReferenceAttachmentId { get; private set; }

    public string? Note { get; private set; }

    public AttachmentRole Role { get; private set; } = AttachmentRole.Regular;

    private Attachment() { }

    public Attachment(
        Guid ownerUserId,
        AttachmentEntityKind kind,
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
        ReferenceAttachmentId = referenceAttachmentId;
        Role = role;
        if (Content is null && string.IsNullOrWhiteSpace(Url) && ReferenceAttachmentId == null)
        {
            throw new ArgumentException("Either content, URL, or reference must be provided for an attachment.");
        }
    }

    public void SetCategory(Guid? categoryId) => CategoryId = categoryId;
    public void SetNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public void SetReference(Guid? referenceId)
    {
        ReferenceAttachmentId = referenceId;
    }

    public void SetRole(AttachmentRole role) => Role = role;

    public void Rename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name required", nameof(fileName));
        }
        var trimmed = fileName.Trim();
        var oldExt = Path.GetExtension(FileName) ?? string.Empty; // includes dot
        var newExt = Path.GetExtension(trimmed) ?? string.Empty;
        if (!string.IsNullOrEmpty(oldExt) && !string.Equals(oldExt, newExt, StringComparison.OrdinalIgnoreCase))
        {
            // Extension changed ? append old extension to keep it
            trimmed += oldExt;
        }
        FileName = trimmed;
    }

    public void Reassign(AttachmentEntityKind toKind, Guid toEntityId)
    {
        EntityKind = toKind;
        EntityId = toEntityId;
    }
}
