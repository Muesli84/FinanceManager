using FinanceManager.Tests.TestHelpers;
using FinanceManager.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Tests.Infrastructure;

/// <summary>
/// Covers <see cref="RequestLoggingMiddleware"/>: it redacts a "token" query parameter from request logs
/// case-insensitively - guarding against download-link access tokens (e.g. for attachments) leaking into log
/// files - while leaving other query parameters intact and still logging the request even when the downstream
/// pipeline throws.
/// </summary>
public sealed class RequestLoggingMiddlewareTests
{
    /// <summary>
    /// Verifies that a "token" query parameter is redacted in the logged message regardless of its casing
    /// (lower/mixed/upper), while an unrelated parameter on the same request stays visible in the log.
    /// </summary>
    /// <param name="parameterName">The casing variant of the "token" query parameter name to test.</param>
    [Theory]
    [InlineData("token")]
    [InlineData("Token")]
    [InlineData("TOKEN")]
    public async Task InvokeAsync_Success_RedactsTokenQueryParameter(string parameterName)
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            logger);
        var context = CreateContext($"/api/attachments/id/download?{parameterName}=super-secret&page=1");

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.DoesNotContain("super-secret", entry.FormattedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", entry.StateText, StringComparison.Ordinal);
        Assert.Contains($"{parameterName}=%5BREDACTED%5D", entry.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("page=1", entry.FormattedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the token redaction still applies on the exception path - the middleware must log (at
    /// warning level) and rethrow when the downstream pipeline throws, and the redaction cannot be skipped just
    /// because the request failed.
    /// </summary>
    [Fact]
    public async Task InvokeAsync_Exception_RedactsTokenQueryParameter()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            _ => Task.FromException(new InvalidOperationException("boom")),
            logger);
        var context = CreateContext("/api/attachments/id/download?token=exception-secret&foo=bar");

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.DoesNotContain("exception-secret", entry.FormattedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-secret", entry.StateText, StringComparison.Ordinal);
        Assert.Contains("token=%5BREDACTED%5D", entry.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("foo=bar", entry.FormattedMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that when a request has no sensitive parameter at all, the logging pipeline leaves the query
    /// string untouched - the redaction logic must be additive and not accidentally mangle ordinary requests
    /// (e.g. ones that end in a non-2xx status).
    /// </summary>
    [Fact]
    public async Task InvokeAsync_PreservesNonSensitiveQueryParameters()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            },
            logger);
        var context = CreateContext("/api/search?foo=bar&page=1");

        await middleware.InvokeAsync(context);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("foo=bar", entry.FormattedMessage, StringComparison.Ordinal);
        Assert.Contains("page=1", entry.FormattedMessage, StringComparison.Ordinal);
    }

    private static DefaultHttpContext CreateContext(string pathAndQuery)
    {
        var split = pathAndQuery.Split('?', 2);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = split[0];
        context.Request.QueryString = split.Length == 2 ? new QueryString("?" + split[1]) : QueryString.Empty;
        context.TraceIdentifier = "trace-test";
        return context;
    }
}
