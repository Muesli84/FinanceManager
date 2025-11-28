using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request sent to create a new attachment category.
/// </summary>
public sealed record AttachmentCreateCategoryRequest([Required, MinLength(2)] string Name);
