namespace FinanceManager.Application.Accounts;

/// <summary>
/// Resolves user time zone identifiers to runtime time zone instances.
/// </summary>
public interface ITimeZoneResolver
{
    /// <summary>
    /// Resolves a configured time zone id, falling back to UTC when it is missing or invalid.
    /// </summary>
    /// <param name="timeZoneId">The time zone id.</param>
    /// <returns>The result.</returns>
    TimeZoneInfo ResolveOrUtc(string? timeZoneId);
}
