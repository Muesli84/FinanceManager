using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request sent to rename an attachment category.
/// </summary>
/// <param name="Name">The name.</param>
/// <returns>The result.</returns>
public sealed record AttachmentUpdateCategoryNameRequest([Required, MinLength(2)] string Name);
