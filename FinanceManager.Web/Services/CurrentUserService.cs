using FinanceManager.Application;
using System.IdentityModel.Tokens.Jwt; // for JwtRegisteredClaimNames
using System.Security.Claims;

namespace FinanceManager.Web.Services;

/// <summary>
/// Provides information about the current HTTP user based on the <see cref="HttpContext"/> user principal.
/// This implementation reads common claims (NameIdentifier / sub) and exposes convenience properties used by the UI and services.
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    /// <summary>
    /// Initializes a new instance of <see cref="CurrentUserService"/>.
    /// </summary>
    /// <param name="http">HTTP context accessor used to obtain the current request principal.</param>
    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    /// <summary>
    /// Gets the current user's identifier as a <see cref="Guid"/> parsed from the standard name identifier claim or the JWT subject claim.
    /// Returns <see cref="Guid.Empty"/> when no authenticated principal is available or the claim value cannot be parsed.
    /// </summary>
    public Guid UserId
    {
        get
        {
            var principal = User;
            if (principal == null) return Guid.Empty;
            var idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(idValue, out var id) ? id : Guid.Empty;
        }
    }

    /// <summary>
    /// Gets the preferred language for the current user (value of the "pref_lang" claim) or <c>null</c> when not present.
    /// </summary>
    /// <returns>The result.</returns>
    public string? PreferredLanguage => User?.FindFirstValue("pref_lang");

    /// <summary>
    /// Gets a value indicating whether the current user is authenticated.
    /// </summary>
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Gets a value indicating whether the current user is in the "Admin" role.
    /// </summary>
    /// <returns>Whether the operation succeeded.</returns>
    public bool IsAdmin => User?.IsInRole("Admin") ?? false;

    /// <summary>
    /// Helper to retrieve the current <see cref="ClaimsPrincipal"/> from the HTTP context, or <c>null</c> when no context is available.
    /// </summary>
    private ClaimsPrincipal? User => _http.HttpContext?.User;
}
