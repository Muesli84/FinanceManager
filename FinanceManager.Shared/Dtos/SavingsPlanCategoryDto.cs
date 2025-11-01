using System;

namespace FinanceManager.Shared.Dtos;

public sealed class SavingsPlanCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? SymbolAttachmentId { get; set; }
}