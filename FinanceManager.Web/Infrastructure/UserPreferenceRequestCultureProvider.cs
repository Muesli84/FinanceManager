using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using FinanceManager.Infrastructure;

namespace FinanceManager.Web.Infrastructure;

public sealed class UserPreferenceRequestCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var userIdClaim = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            return null;
        }

        // Scoped DbContext abrufen
        var db = httpContext.RequestServices.GetService<AppDbContext>();
        if (db == null)
        {
            return null;
        }

        var lang = await db.Users
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