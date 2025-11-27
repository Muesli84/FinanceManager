using System;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO representing an attachment including metadata about its origin and storage.
/// </summary>
/// <param name="Id">Unique attachment identifier.</param>
/// <param name="EntityKind">Numeric kind of the parent entity the attachment belongs to.</param>
/// <param name="EntityId">Identifier of the parent entity.</param>
/// <param name="FileName">Original file name.</param>
/// <param name="ContentType">MIME content type.</param>
/// <param name="SizeBytes">Size of the file in bytes.</param>
/// <param name="CategoryId">Optional category id the attachment is associated with.</param>
/// <param name="UploadedUtc">Upload timestamp in UTC.</param>
/// <param name="IsUrl">True when the attachment represents a URL instead of stored content.</param>
public sealed record AttachmentDto(
    Guid Id,
    short EntityKind,
    Guid EntityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid? CategoryId,
    DateTime UploadedUtc,
    bool IsUrl,
    short Role = 0
);
