namespace FinanceManager.Shared.Dtos.Securities;

/// <summary>
/// DTO describing a security (stock or fund) including categorization and status metadata.
/// </summary>
public sealed class SecurityDto
{
    /// <summary>Unique security identifier.</summary>
    public Guid Id { get; set; }
    /// <summary>Display name of the security.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional description.</summary>
    public string? Description { get; set; }
    /// <summary>Primary identifier (e.g., ISIN, ticker).</summary>
    public string Identifier { get; set; } = string.Empty;
    /// <summary>Optional AlphaVantage code for price lookups.</summary>
    public string? AlphaVantageCode { get; set; }
    /// <summary>Currency code (ISO 4217).</summary>
    public string CurrencyCode { get; set; } = "EUR";
    /// <summary>Optional category id.</summary>
    public Guid? CategoryId { get; set; }
    /// <summary>Optional category name for display.</summary>
    public string? CategoryName { get; set; }
    /// <summary>Indicates whether the security is currently active.</summary>
    public bool IsActive { get; set; }
    /// <summary>UTC timestamp when the security was created.</summary>
    public DateTime CreatedUtc { get; set; }
    /// <summary>UTC timestamp when the security was archived, if any.</summary>
    public DateTime? ArchivedUtc { get; set; }
    /// <summary>Optional symbol attachment id.</summary>
    public Guid? SymbolAttachmentId { get; set; }
}

/// <summary>
/// Transaction types for security postings.
/// </summary>
public enum SecurityTransactionType
{
    /// <summary>Buy transaction.</summary>
    Buy = 0,
    /// <summary>Sell transaction.</summary>
    Sell = 1,
    /// <summary>Dividend transaction.</summary>
    Dividend = 2
}

/// <summary>
/// Posting sub-types used for detailed security posting categorization.
/// </summary>
public enum SecurityPostingSubType
{
    /// <summary>Buy.</summary>
    Buy = 0,
    /// <summary>Sell.</summary>
    Sell = 1,
    /// <summary>Dividend.</summary>
    Dividend = 2,
    /// <summary>Fee.</summary>
    Fee = 3,
    /// <summary>Tax.</summary>
    Tax = 4
}