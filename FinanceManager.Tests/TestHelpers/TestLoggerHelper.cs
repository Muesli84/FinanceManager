using Microsoft.Extensions.Logging;

namespace FinanceManager.Tests.TestHelpers;

/// <summary>
/// Creates real, console-backed <see cref="ILogger{T}"/> instances for tests that just need a working logger
/// to satisfy a constructor dependency and want log output visible in the test console for debugging. When a
/// test needs to assert on what was logged rather than merely provide a logger, use
/// <see cref="CapturingLogger{T}"/> instead.
/// </summary>
internal static class TestLoggerHelper
{
    /// <summary>Builds a console logger for <typeparamref name="T"/> with minimum level <see cref="LogLevel.Debug"/>.</summary>
    /// <typeparam name="T">The type parameter.</typeparam>
    /// <returns>The result.</returns>
    public static ILogger<T> CreateLogger<T>() where T : class
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        return loggerFactory.CreateLogger<T>();
    }
}
