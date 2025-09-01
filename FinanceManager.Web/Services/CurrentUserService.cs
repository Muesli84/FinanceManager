using FinanceManager.Application;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt; // for JwtRegisteredClaimNames

namespace FinanceManager.Web.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

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
    public string? PreferredLanguage => User?.FindFirstValue("pref_lang");
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
    public bool IsAdmin => (User?.FindFirst("is_admin")?.Value) == "true";

    private ClaimsPrincipal? User => _http.HttpContext?.User;
}
