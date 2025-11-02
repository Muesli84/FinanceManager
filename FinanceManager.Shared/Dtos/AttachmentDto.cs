using System;

namespace FinanceManager.Shared.Dtos;

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
