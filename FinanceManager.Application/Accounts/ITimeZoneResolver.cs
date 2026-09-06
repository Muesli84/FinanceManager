namespace FinanceManager.Application.Accounts;

/// <summary>
/// Resolves user time zone identifiers to runtime time zone instances.
/// </summary>
public interface ITimeZoneResolver
{
    /// <summary>
    /// Resolves a configured time zone id, falling back to UTC when it is missing or invalid.
    /// </summary>
    TimeZoneInfo ResolveOrUtc(string? timeZoneId);
}
