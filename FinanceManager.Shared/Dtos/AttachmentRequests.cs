using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed record AttachmentUpdateCategoryRequest(Guid? CategoryId);
public sealed record AttachmentUpdateCoreRequest(string? FileName, Guid? CategoryId);

public sealed record AttachmentCreateCategoryRequest([Required, MinLength(2)] string Name);
public sealed record AttachmentUpdateCategoryNameRequest([Required, MinLength(2)] string Name);
