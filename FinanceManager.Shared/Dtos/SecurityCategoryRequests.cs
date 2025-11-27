using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

/// <summary>
/// Request payload to create or update a security category.
/// </summary>
public sealed class SecurityCategoryRequest
{
    /// <summary>Display name of the category.</summary>
    [Required, MinLength(2)]
    public string Name { get; set; } = string.Empty;
}
