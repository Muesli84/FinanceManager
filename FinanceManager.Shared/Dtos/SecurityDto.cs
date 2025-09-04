using System;

namespace FinanceManager.Shared.Dtos;

public sealed class SecurityDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public string? AlphaVantageCode { get; set; }
    public string CurrencyCode { get; set; } = "EUR";
    public Guid? CategoryId { get; set; }          // NEW
    public string? CategoryName { get; set; }      // NEW (Anzeige)
    public bool IsActive { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ArchivedUtc { get; set; }
}
