using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

public sealed class JwtCookieAuthTokenProvider : IAuthTokenProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConfiguration _configuration;

    private readonly object _sync = new();
    private string? _cachedToken;
    private DateTimeOffset _cachedExpiry;

    private static readonly TimeSpan MinRenewalWindow = TimeSpan.FromMinutes(5);
    private const string AuthCookieName = "FinanceManager.Auth"; // <- zentraler Name

    public JwtCookieAuthTokenProvider(IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
    {
        _httpContextAccessor = httpContextAccessor;
        _configuration = configuration;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx == null)
        {
            return Task.FromResult<string?>(null);
        }

        var now = DateTimeOffset.UtcNow;

        // Determine renewal window from configured lifetime (half of it)
        var lifetimeMinutes = int.TryParse(_configuration["Jwt:LifetimeMinutes"], out var lm) ? lm : 30;
        var renewalWindow = TimeSpan.FromMinutes(Math.Max(MinRenewalWindow.TotalMinutes, lm / 2.0));

        // Cache noch gültig?
        if (_cachedToken != null && _cachedExpiry - renewalWindow > now)
        {
            return Task.FromResult<string?>(_cachedToken);
        }

        // read cookie by new name
        var cookie = ctx.Request.Cookies[AuthCookieName];
        if (string.IsNullOrEmpty(cookie))
        {
            // Kein Token vorhanden
            InvalidateCache();
            return Task.FromResult<string?>(null);
        }

        var handler = new JwtSecurityTokenHandler();
        var key = _configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            InvalidateCache();
            return Task.FromResult<string?>(null);
        }

        try
        {
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                ClockSkew = TimeSpan.FromSeconds(10)
            };

            var principal = handler.ValidateToken(cookie, parameters, out var validatedToken);
            var jwt = (JwtSecurityToken)validatedToken;
            var exp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Exp).Value));

            // Erneuern wenn bald ablaufend
            if (exp - renewalWindow <= now)
            {
                var refreshed = IssueToken(principal.Claims, lifetimeMinutes);
                SetCookie(ctx, refreshed.token, refreshed.expiry);
                Cache(refreshed.token, refreshed.expiry);
                return Task.FromResult<string?>(refreshed.token);
            }

            Cache(cookie, exp);
            return Task.FromResult<string?>(cookie);
        }
        catch
        {
            InvalidateCache();
            return Task.FromResult<string?>(null);
        }
    }

    // Explicit cache clear for logout
    public void Clear()
    {
        InvalidateCache();
    }

    private (string token, DateTimeOffset expiry) IssueToken(IEnumerable<Claim> claims, int lifetimeMinutes)
    {
        var key = _configuration["Jwt:Key"]!;
        var expiry = DateTimeOffset.UtcNow.AddMinutes(lifetimeMinutes);

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        // Claims filtern: Keine doppelten exp/nbf/iat erneut hinzufügen
        var filtered = claims.Where(c =>
            c.Type != JwtRegisteredClaimNames.Exp &&
            c.Type != JwtRegisteredClaimNames.Nbf &&
            c.Type != JwtRegisteredClaimNames.Iat).ToList();

        // Ensure Admin role claim is present when principal indicates admin
        var hasRoleClaim = filtered.Any(c => c.Type == ClaimTypes.Role || string.Equals(c.Type, "role", StringComparison.OrdinalIgnoreCase));

        var jwt = new JwtSecurityToken(
            claims: filtered,
            notBefore: DateTime.UtcNow,
            expires: expiry.UtcDateTime,
            signingCredentials: creds);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expiry);
    }

    private void SetCookie(HttpContext ctx, string token, DateTimeOffset expiry)
    {
        ctx.Response.Cookies.Append(AuthCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiry,
            Path = "/"
        });
    }

    private void Cache(string token, DateTimeOffset expiry)
    {
        lock (_sync)
        {
            _cachedToken = token;
            _cachedExpiry = expiry;
        }
    }

    private void InvalidateCache()
    {
        lock (_sync)
        {
            _cachedToken = null;
            _cachedExpiry = DateTimeOffset.MinValue;
        }
    }
}