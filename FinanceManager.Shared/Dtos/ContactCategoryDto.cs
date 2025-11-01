using System;

namespace FinanceManager.Shared.Dtos;

public sealed record ContactCategoryDto(Guid Id, string Name, Guid? SymbolAttachmentId = null);
