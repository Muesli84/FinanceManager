using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed class SecurityCategoryRequest
{
    [Required, MinLength(2)]
    public string Name { get; set; } = string.Empty;
}
