namespace FinanceManager.Domain.Users;

public sealed partial class User
{
    public string? AlphaVantageApiKey { get; private set; }
    public bool ShareAlphaVantageApiKey { get; private set; }

    public void SetAlphaVantageKey(string? apiKey)
    {
        // allow clearing with null/empty
        AlphaVantageApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        Touch();
    }

    public void SetShareAlphaVantageKey(bool share)
    {
        ShareAlphaVantageApiKey = share;
        Touch();
    }
}