using System;

namespace FinanceManager.Shared.Dtos;

public sealed record AttachmentCategoryDto(Guid Id, string Name, bool IsSystem, bool InUse);
