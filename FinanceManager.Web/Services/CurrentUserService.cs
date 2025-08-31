using FinanceManager.Application;
using System.Security.Claims;

namespace FinanceManager.Web.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    public Guid UserId => Guid.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    public string? PreferredLanguage => User?.FindFirstValue("pref_lang");
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
    public bool IsAdmin => (User?.FindFirst("is_admin")?.Value) == "true";

    private ClaimsPrincipal? User => _http.HttpContext?.User;
}
