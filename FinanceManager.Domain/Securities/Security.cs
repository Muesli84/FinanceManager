using System;

namespace FinanceManager.Domain.Securities;

public sealed class Security
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Identifier { get; private set; } = string.Empty; // WKN / ISIN
    public string? AlphaVantageCode { get; private set; }
    public string CurrencyCode { get; private set; } = "EUR";
    public Guid? CategoryId { get; private set; }          // NEW
    public DateTime CreatedUtc { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? ArchivedUtc { get; private set; }

    // NEW: Price fetch error state
    public bool HasPriceError { get; private set; }
    public string? PriceErrorMessage { get; private set; }
    public DateTime? PriceErrorSinceUtc { get; private set; }

    private Security() { }

    public Security(Guid ownerUserId, string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Update(name, identifier, description, alphaVantageCode, currencyCode, categoryId);
        CreatedUtc = DateTime.UtcNow;
        IsActive = true;
    }

    public void Update(string name, string identifier, string? description, string? alphaVantageCode, string currencyCode, Guid? categoryId)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("Name required", nameof(name)); }
        if (string.IsNullOrWhiteSpace(identifier)) { throw new ArgumentException("Identifier required", nameof(identifier)); }
        if (string.IsNullOrWhiteSpace(currencyCode)) { throw new ArgumentException("Currency required", nameof(currencyCode)); }

        Name = name.Trim();
        Identifier = identifier.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AlphaVantageCode = string.IsNullOrWhiteSpace(alphaVantageCode) ? null : alphaVantageCode.Trim();
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        CategoryId = categoryId;
    }

    public void Archive()
    {
        if (!IsActive) { return; }
        IsActive = false;
        ArchivedUtc = DateTime.UtcNow;
    }

    // NEW: mark/unmark price error
    public void SetPriceError(string message)
    {
        HasPriceError = true;
        PriceErrorMessage = string.IsNullOrWhiteSpace(message) ? "Unknown error" : message;
        PriceErrorSinceUtc = DateTime.UtcNow;
    }

    public void ClearPriceError()
    {
        HasPriceError = false;
        PriceErrorMessage = null;
        PriceErrorSinceUtc = null;
    }
}
