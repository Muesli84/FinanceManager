using System;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO representing an attachment category used to organize attachments.
/// </summary>
/// <param name="Id">Unique identifier of the category.</param>
/// <param name="Name">Display name of the category.</param>
/// <param name="IsSystem">True when the category is system-managed.</param>
/// <param name="IsDefault">True when the category is the default selection.</param>
public sealed record AttachmentCategoryDto(Guid Id, string Name, bool IsSystem, bool IsDefault);
