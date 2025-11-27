using System.ComponentModel.DataAnnotations;

namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// Request payload to create or update a security.
/// </summary>
public sealed class SecurityRequest
{
    /// <summary>Display name of the security.</summary>
    [Required, MinLength(2)] public string Name { get; set; } = string.Empty;
    /// <summary>Primary identifier (e.g., ISIN, ticker).</summary>
    [Required, MinLength(3)] public string Identifier { get; set; } = string.Empty;
    /// <summary>Currency code (ISO 4217).</summary>
    [Required, MinLength(3)] public string CurrencyCode { get; set; } = "EUR";
    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
    /// <summary>Optional AlphaVantage code for price lookups.</summary>
    public string? AlphaVantageCode { get; set; }
    /// <summary>Optional category id.</summary>
    public Guid? CategoryId { get; set; }
}
