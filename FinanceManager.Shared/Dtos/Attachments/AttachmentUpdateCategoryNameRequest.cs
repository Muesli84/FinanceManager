using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request sent to rename an attachment category.
/// </summary>
public sealed record AttachmentUpdateCategoryNameRequest([Required, MinLength(2)] string Name);
