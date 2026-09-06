using FinanceManager.Application.Accounts;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Infrastructure.Accounts;

/// <summary>
/// Resolves configured user time zones and falls back to UTC for missing or invalid values.
/// </summary>
public sealed class TimeZoneResolver : ITimeZoneResolver
{
    private readonly ILogger<TimeZoneResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimeZoneResolver"/> class.
    /// </summary>
    public TimeZoneResolver(ILogger<TimeZoneResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public TimeZoneInfo ResolveOrUtc(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException ex)
        {
            _logger.LogWarning(ex, "Configured account statistics time zone is unknown; falling back to UTC.");
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException ex)
        {
            _logger.LogWarning(ex, "Configured account statistics time zone is invalid; falling back to UTC.");
            return TimeZoneInfo.Utc;
        }
    }
}
