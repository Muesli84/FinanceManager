using System;

namespace FinanceManager.Shared.Dtos;

public sealed class SecurityCategoryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}