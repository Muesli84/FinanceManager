using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FinanceManager.Infrastructure.Auth;

public interface IJwtTokenService
{
    string CreateToken(Guid userId, string username, bool isAdmin, DateTime expiresUtc, string? preferredLanguage = null, string? timeZoneId = null);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;
    public JwtTokenService(IConfiguration config) => _config = config;

    public string CreateToken(Guid userId, string username, bool isAdmin, DateTime expiresUtc, string? preferredLanguage = null, string? timeZoneId = null)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
        var issuer = _config["Jwt:Issuer"] ?? "financemanager";
        var audience = _config["Jwt:Audience"] ?? issuer;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()), // ensure claim recognized by CurrentUserService
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.UniqueName, username)
        };
        if (isAdmin)
        {
            // use standard role claim instead of custom is_admin flag
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            claims.Add(new Claim("pref_lang", preferredLanguage));
        }
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            claims.Add(new Claim("tz", timeZoneId));
        }
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore: DateTime.UtcNow, expires: expiresUtc, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
