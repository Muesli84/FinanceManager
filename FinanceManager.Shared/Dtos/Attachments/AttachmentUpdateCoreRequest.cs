namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request payload to update core attachment metadata such as file name and category.
/// </summary>
public sealed record AttachmentUpdateCoreRequest(
    /// <summary>New file name to set; when null the existing value is kept.</summary>
    string? FileName,
    /// <summary>New category identifier to set; when null the existing value is kept.</summary>
    Guid? CategoryId
);
