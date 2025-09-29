using System.Text.Json;
using FinanceManager.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinanceManager.Web.Infrastructure;

/// <summary>
/// Middleware that denies requests from IP addresses present in the IP block list.
/// </summary>
public sealed class IpBlockMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpBlockMiddleware> _logger;

    public IpBlockMiddleware(RequestDelegate next, ILogger<IpBlockMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ip = context.Connection.RemoteIpAddress?.ToString();

        if (!string.IsNullOrWhiteSpace(ip))
        {
            bool isBlocked = await db.IpBlocks.AsNoTracking()
                .AnyAsync(b => b.IpAddress == ip && b.IsBlocked, context.RequestAborted);

            if (isBlocked)
            {
                _logger.LogWarning("Blocked request from IP {Ip} to {Path}", ip, context.Request.Path);

                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    var payload = new
                    {
                        title = "IP blocked",
                        status = StatusCodes.Status403Forbidden,
                        detail = "This IP address is currently blocked.",
                        ip,
                        traceId = context.TraceIdentifier
                    };
                    await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
                }
                return;
            }
        }

        await _next(context);
    }
}
