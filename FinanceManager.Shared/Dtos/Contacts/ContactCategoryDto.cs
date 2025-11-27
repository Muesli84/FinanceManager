namespace FinanceManager.Shared.Dtos.Contacts;

public sealed record ContactCategoryDto(Guid Id, string Name, Guid? SymbolAttachmentId = null);
