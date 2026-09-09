using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Attachments;

/// <summary>
/// Request sent to create a new attachment category.
/// </summary>
/// <param name="Name">The name.</param>
/// <returns>The result.</returns>
public sealed record AttachmentCreateCategoryRequest([Required, MinLength(2)] string Name);
