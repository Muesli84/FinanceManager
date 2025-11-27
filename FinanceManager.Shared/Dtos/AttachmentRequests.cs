using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to update core attachment metadata such as file name and category.
/// </summary>
public sealed record AttachmentUpdateCoreRequest(
    /// <summary>New file name to set; when null the existing value is kept.</summary>
    string? FileName,
    /// <summary>New category identifier to set; when null the existing value is kept.</summary>
    Guid? CategoryId
);

/// <summary>
/// Request payload to update only the category of an attachment.
/// </summary>
public sealed record AttachmentUpdateCategoryRequest(
    /// <summary>The category identifier to assign; null clears the category.</summary>
    Guid? CategoryId
);

/// <summary>
/// Request sent to create a new attachment category.
/// </summary>
public sealed record AttachmentCreateCategoryRequest([Required, MinLength(2)] string Name);

/// <summary>
/// Request sent to rename an attachment category.
/// </summary>
public sealed record AttachmentUpdateCategoryNameRequest([Required, MinLength(2)] string Name);
