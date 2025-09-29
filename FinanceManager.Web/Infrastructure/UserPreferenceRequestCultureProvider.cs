using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Infrastructure;

namespace FinanceManager.Web.Infrastructure;

/// <summary>
/// Resolves request culture from user preferences.
/// Order of resolution:
/// 1) JWT claim "pref_lang" (set at login/registration)
/// 2) Database fallback (User.PreferredLanguage)
/// 3) null -> next provider (cookie/query/header)
/// </summary>
public sealed class UserPreferenceRequestCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // 1) Try JWT claim first (no DB access)
        var prefLangClaim = httpContext.User.FindFirst("pref_lang")?.Value;
        if (!string.IsNullOrWhiteSpace(prefLangClaim))
        {
            try
            {
                var culture = new CultureInfo(prefLangClaim);
                return new ProviderCultureResult(culture.Name, culture.Name);
            }
            catch (CultureNotFoundException)
            {
                // ignore and fallback to DB or next providers
            }
        }

        // 2) DB fallback
        var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return null;
        }

        var db = httpContext.RequestServices.GetService<AppDbContext>();
        if (db == null)
        {
            return null;
        }

        var lang = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.PreferredLanguage != null)
            .Select(u => u.PreferredLanguage)
            .FirstOrDefaultAsync(httpContext.RequestAborted);

        if (string.IsNullOrWhiteSpace(lang))
        {
            return null;
        }

        try
        {
            var culture = new CultureInfo(lang);
            return new ProviderCultureResult(culture.Name, culture.Name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}