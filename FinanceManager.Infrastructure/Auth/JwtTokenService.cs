using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace FinanceManager.Infrastructure.Auth;

public interface IJwtTokenService
{
    string CreateToken(Guid userId, string username, bool isAdmin, DateTime expiresUtc);
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;
    public JwtTokenService(IConfiguration config) => _config = config;

    public string CreateToken(Guid userId, string username, bool isAdmin, DateTime expiresUtc)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing");
        var issuer = _config["Jwt:Issuer"] ?? "financemanager";
        var audience = _config["Jwt:Audience"] ?? issuer;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(ClaimTypes.NameIdentifier, userId.ToString()), // ensure claim recognized by CurrentUserService
            new(ClaimTypes.Name, username),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new("is_admin", isAdmin ? "true" : "false")
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore: DateTime.UtcNow, expires: expiresUtc, signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
