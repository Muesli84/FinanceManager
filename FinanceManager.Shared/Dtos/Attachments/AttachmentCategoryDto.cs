namespace FinanceManager.Shared.Dtos.Attachments;

public sealed record AttachmentCategoryDto(Guid Id, string Name, bool IsSystem, bool IsDefault);
