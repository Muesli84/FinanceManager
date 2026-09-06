using FinanceManager.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;

namespace FinanceManager.Web.Infrastructure.Auth
{
    /// <summary>
    /// Provides access tokens based on the authentication cookie. The provider reads the JWT stored in the
    /// configured authentication cookie, validates it and returns the token string. When the token is close to
    /// expiry the provider will issue a refreshed token and set it as a response cookie.
    /// </summary>
    public sealed class JwtCookieAuthTokenProvider : IAuthTokenProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptions<JwtOptions> _options;
        private readonly JwtTokenValidationParametersFactory _validationParametersFactory;
        private readonly IJwtRefreshService _refreshService;

        private readonly object _sync = new();
        private string? _cachedToken;
        private DateTimeOffset _cachedExpiry;

        private static readonly TimeSpan MinRenewalWindow = TimeSpan.FromMinutes(5);
        private const string AuthCookieName = "FinanceManager.Auth"; // <- zentraler Name

        /// <summary>
        /// Initializes a new instance of the <see cref="JwtCookieAuthTokenProvider"/> class.
        /// </summary>
        /// <param name="httpContextAccessor">Accessor to obtain the current HTTP context (required).</param>
        /// <param name="options">Validated JWT settings.</param>
        /// <param name="validationParametersFactory">Factory for shared JWT validation parameters.</param>
        /// <param name="refreshService">Service that validates and renews JWTs against current user state.</param>
        /// <exception cref="ArgumentNullException">Thrown when either argument is <c>null</c>.</exception>
        public JwtCookieAuthTokenProvider(
            IHttpContextAccessor httpContextAccessor,
            IOptions<JwtOptions> options,
            JwtTokenValidationParametersFactory validationParametersFactory,
            IJwtRefreshService refreshService)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _validationParametersFactory = validationParametersFactory ?? throw new ArgumentNullException(nameof(validationParametersFactory));
            _refreshService = refreshService ?? throw new ArgumentNullException(nameof(refreshService));
        }

        /// <summary>
        /// Asynchronously obtains an access token string suitable for authorizing outgoing HTTP requests.
        /// The provider reads the JWT from the authentication cookie, validates it and returns the token.
        /// When the token is near expiry a refreshed token is issued and written to the response cookie;
        /// the refreshed token is returned and cached for subsequent calls.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token used to cancel the operation.</param>
        /// <returns>
        /// A task that resolves to the access token string when available; or <c>null</c> when no valid token could be obtained.
        /// </returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled via the provided <paramref name="cancellationToken"/>.</exception>
        /// <exception cref="InvalidOperationException">May be thrown when the provider is not correctly configured (for example missing signing key) - implementations may also return <c>null</c> instead of throwing.</exception>
        public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
        {
            var ctx = _httpContextAccessor.HttpContext;
            var now = DateTimeOffset.UtcNow;

            // Determine renewal window from configured lifetime (half of it)
            var lifetimeMinutes = _options.Value.LifetimeMinutes;
            var renewalWindow = TimeSpan.FromMinutes(Math.Max(MinRenewalWindow.TotalMinutes, lifetimeMinutes / 2.0));

            // Prefer request cookie over cache when a request context is available.
            // This prevents serving stale tokens from a different browser tab/user session.
            if (ctx != null)
            {
                var cookie = ctx.Request.Cookies[AuthCookieName];
                if (string.IsNullOrEmpty(cookie))
                {
                    InvalidateCache();
                    return null;
                }

                if (_cachedToken != null
                    && string.Equals(_cachedToken, cookie, StringComparison.Ordinal)
                    && _cachedExpiry - renewalWindow > now)
                {
                    return _cachedToken;
                }

                return await ValidateAndRefreshTokenAsync(ctx, cookie, renewalWindow, now, cancellationToken);
            }

            // When no request context is available (for example within a running Blazor circuit),
            // fall back to a still-valid cached token.
            if (_cachedToken != null && _cachedExpiry - renewalWindow > now)
            {
                return _cachedToken;
            }

            return null;
        }

        private async Task<string?> ValidateAndRefreshTokenAsync(
            HttpContext ctx,
            string cookie,
            TimeSpan renewalWindow,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            var handler = new JwtSecurityTokenHandler();

            try
            {
                var parameters = _validationParametersFactory.Create();

                var principal = handler.ValidateToken(cookie, parameters, out var validatedToken);
                var jwt = (JwtSecurityToken)validatedToken;
                var exp = DateTimeOffset.FromUnixTimeSeconds(long.Parse(jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Exp).Value));

                // Erneuern wenn bald ablaufend
                if (exp - renewalWindow <= now)
                {
                    var refreshed = await _refreshService.RefreshAsync(principal, cancellationToken);
                    if (!refreshed.Succeeded || refreshed.Token is null || refreshed.ExpiresUtc is null)
                    {
                        InvalidateCache();
                        DeleteCookie(ctx);
                        return null;
                    }

                    var expiry = new DateTimeOffset(refreshed.ExpiresUtc.Value);
                    SetCookie(ctx, refreshed.Token, expiry);
                    Cache(refreshed.Token, expiry);
                    return refreshed.Token;
                }

                Cache(cookie, exp);
                return cookie;
            }
            catch
            {
                InvalidateCache();
                return null;
            }
        }

        /// <summary>
        /// Clears any internally cached token. Call this when the user logs out to ensure subsequent calls don't return a stale token.
        /// </summary>
        public void Clear()
        {
            InvalidateCache();
        }

        /// <inheritdoc/>
        public void InvalidateCache()
        {
            lock (_sync)
            {
                _cachedToken = null;
                _cachedExpiry = DateTimeOffset.MinValue;
            }
        }

        /// <summary>
        /// Stores a known-valid token for later use when no request context is available, for example in a Blazor circuit.
        /// </summary>
        /// <param name="token">Serialized JWT token.</param>
        /// <param name="expiry">Token expiry timestamp.</param>
        public void PrimeCache(string token, DateTimeOffset expiry)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                InvalidateCache();
                return;
            }

            Cache(token, expiry);
        }

        /// <summary>
        /// Writes the authentication cookie to the response.
        /// </summary>
        /// <param name="ctx">Current HTTP context.</param>
        /// <param name="token">Token string to set in the cookie.</param>
        /// <param name="expiry">Expiry timestamp for the cookie.</param>
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

        private static void DeleteCookie(HttpContext ctx)
        {
            ctx.Response.Cookies.Delete(AuthCookieName, new CookieOptions
            {
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });
        }

        /// <summary>
        /// Caches the token and its expiry in-memory for quick subsequent access.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <param name="expiry">The expiry.</param>
        private void Cache(string token, DateTimeOffset expiry)
        {
            lock (_sync)
            {
                _cachedToken = token;
                _cachedExpiry = expiry;
            }
        }

    }
}
