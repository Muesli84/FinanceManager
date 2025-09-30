namespace FinanceManager.Domain.Users;

public sealed partial class User
{
    /// <summary>
    /// IANA time zone identifier (e.g., "Europe/Berlin"). Optional; null means not set.
    /// </summary>
    public string? TimeZoneId { get; private set; }

    public void SetTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            TimeZoneId = null; // unset
            Touch();
            return;
        }
        var trimmed = timeZoneId.Trim();
        if (trimmed.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(timeZoneId), "TimeZoneId must be <= 100 characters.");
        }
        TimeZoneId = trimmed;
        Touch();
    }
}
