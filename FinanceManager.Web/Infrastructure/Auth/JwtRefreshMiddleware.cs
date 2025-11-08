using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using FinanceManager.Infrastructure.Auth;
using System.Linq;

namespace FinanceManager.Web.Infrastructure.Auth
{
    public sealed class JwtRefreshMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;

        private const string RefreshHeaderName = "X-Auth-Token";
        private const string RefreshExpiresHeaderName = "X-Auth-Token-Expires";

        public JwtRefreshMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                if (context.User?.Identity?.IsAuthenticated != true)
                {
                    return;
                }

                var token = GetIncomingToken(context);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return;
                }

                var handler = new JwtSecurityTokenHandler();
                JwtSecurityToken? jwt;
                try
                {
                    jwt = handler.ReadJwtToken(token);
                }
                catch
                {
                    return;
                }

                var expClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
                if (string.IsNullOrEmpty(expClaim) || !long.TryParse(expClaim, out var expSec))
                {
                    return;
                }

                var exp = DateTimeOffset.FromUnixTimeSeconds(expSec);
                var now = DateTimeOffset.UtcNow;

                // Determine renewal window dynamically based on configured lifetime
                var lifetimeMinutes = int.TryParse(_configuration["Jwt:LifetimeMinutes"], out var lm) ? lm : 30;
                var renewalWindow = TimeSpan.FromMinutes(Math.Max(5, lm / 2)); // renew when half of lifetime has passed

                if (exp - renewalWindow > now)
                {
                    return;
                }

                var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var username = context.User.Identity?.Name
                               ?? context.User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
                               ?? context.User.FindFirstValue(ClaimTypes.Name)
                               ?? string.Empty;
                var isAdmin = context.User.IsInRole("Admin");

                if (!Guid.TryParse(userIdStr, out var userId))
                {
                    return;
                }

                var jts = context.RequestServices.GetRequiredService<IJwtTokenService>();
                var newToken = jts.CreateToken(userId, username, isAdmin, out var newExpiry);

                if (!context.Response.HasStarted)
                {
                    context.Response.Cookies.Append("fm_auth", newToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = context.Request.IsHttps, // vorher: true
                        SameSite = SameSiteMode.Lax,
                        Expires = new DateTimeOffset(newExpiry),
                        Path = "/"
                    });
                }

                context.Response.Headers[RefreshHeaderName] = newToken;
                context.Response.Headers[RefreshExpiresHeaderName] = newExpiry.ToString("o");
            }
            finally
            {
                await _next(context);
            }
        }

        private static string? GetIncomingToken(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("Authorization", out var authVals))
            {
                var auth = authVals.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return auth.Substring("Bearer ".Length).Trim();
                }
            }

            if (context.Request.Cookies.TryGetValue("fm_auth", out var cookie) && !string.IsNullOrWhiteSpace(cookie))
            {
                return cookie;
            }
            return null;
        }
    }
}
