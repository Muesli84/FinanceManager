using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos;

public sealed class SecurityRequest
{
    [Required, MinLength(2)] public string Name { get; set; } = string.Empty;
    [Required, MinLength(3)] public string Identifier { get; set; } = string.Empty;
    [Required, MinLength(3)] public string CurrencyCode { get; set; } = "EUR";
    public string? Description { get; set; }
    public string? AlphaVantageCode { get; set; }
    public Guid? CategoryId { get; set; }
}
