using System;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// DTO describing a savings plan category.
/// </summary>
public sealed class SavingsPlanCategoryDto
{
    /// <summary>Unique category identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Display name of the category.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional symbol attachment id associated with the category.</summary>
    public Guid? SymbolAttachmentId { get; set; }
}