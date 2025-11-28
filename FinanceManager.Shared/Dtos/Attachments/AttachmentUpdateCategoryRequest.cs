namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request payload to update only the category of an attachment.
/// </summary>
public sealed record AttachmentUpdateCategoryRequest(
    /// <summary>The category identifier to assign; null clears the category.</summary>
    Guid? CategoryId
);
