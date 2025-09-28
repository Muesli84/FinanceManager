using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Infrastructure;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
            sw.Stop();

            var status = context.Response?.StatusCode ?? 0;
            var level = status < 400 ? LogLevel.Debug : LogLevel.Warning;
            var method = context.Request.Method;
            var path = context.Request.Path + context.Request.QueryString;
            var elapsedMs = sw.ElapsedMilliseconds;
            var traceId = context.TraceIdentifier;

            _logger.Log(level,
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms (TraceId: {TraceId})",
                method, path, status, elapsedMs, traceId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var method = context.Request.Method;
            var path = context.Request.Path + context.Request.QueryString;
            var elapsedMs = sw.ElapsedMilliseconds;
            var traceId = context.TraceIdentifier;

            _logger.LogWarning(ex,
                "HTTP {Method} {Path} threw in {ElapsedMs} ms (TraceId: {TraceId})",
                method, path, elapsedMs, traceId);

            throw;
        }
    }
}