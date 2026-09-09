namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request payload to update core attachment metadata such as file name and category.
/// </summary>
/// <param name="FileName">New file name to set; when null the existing value is kept.</param>
/// <param name="CategoryId">New category identifier to set; when null the existing value is kept.</param>
/// <returns>The result.</returns>
public sealed record AttachmentUpdateCoreRequest(
    string? FileName,
    Guid? CategoryId
);
