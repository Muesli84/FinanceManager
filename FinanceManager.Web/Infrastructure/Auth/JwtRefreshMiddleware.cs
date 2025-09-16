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
        private static readonly TimeSpan RenewalWindow = TimeSpan.FromMinutes(60);

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
                if (exp - RenewalWindow > now)
                {
                    return;
                }

                var lifetimeMinutes = int.TryParse(_configuration["Jwt:LifetimeMinutes"], out var lm) ? lm : 30;
                var newExpiry = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes);

                var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                                  ?? context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
                var username = context.User.Identity?.Name
                               ?? context.User.FindFirstValue(JwtRegisteredClaimNames.UniqueName)
                               ?? context.User.FindFirstValue(ClaimTypes.Name)
                               ?? string.Empty;
                var isAdmin = (context.User.FindFirst("is_admin")?.Value)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;

                if (!Guid.TryParse(userIdStr, out var userId))
                {
                    return;
                }

                var jts = context.RequestServices.GetRequiredService<IJwtTokenService>();
                var newToken = jts.CreateToken(userId, username, isAdmin, newExpiry.UtcDateTime);

                // Setze Cookie früh in der Pipeline, bevor die Antwort startet
                if (!context.Response.HasStarted)
                {
                    context.Response.Cookies.Append("fm_auth", newToken, new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Expires = newExpiry
                    });
                }

                // Optional: Header weiterhin setzen (diagnostisch/Clients ohne Cookies)
                context.Response.Headers[RefreshHeaderName] = newToken;
                context.Response.Headers[RefreshExpiresHeaderName] = newExpiry.UtcDateTime.ToString("o");
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
