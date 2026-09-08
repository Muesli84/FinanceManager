using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FinanceManager.Domain.Users;
using FinanceManager.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace FinanceManager.Web.Infrastructure.Auth;

/// <summary>
/// Reissues JWTs after validating the token principal against current server-side user state.
/// </summary>
public interface IJwtRefreshService
{
    /// <summary>
    /// Validates the supplied principal and returns a renewed JWT when the user is active and the security stamp matches.
    /// </summary>
    /// <param name="principal">Claims principal created from the existing JWT.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A refresh result containing the renewed token or the rejection reason.</returns>
    Task<JwtRefreshResult> RefreshAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}

/// <summary>
/// Result returned by JWT refresh attempts.
/// </summary>
/// <param name="Succeeded">Whether refresh succeeded.</param>
/// <param name="Token">Renewed JWT when refresh succeeded.</param>
/// <param name="ExpiresUtc">Renewed token expiry in UTC when refresh succeeded.</param>
/// <param name="FailureReason">Diagnostic rejection reason when refresh failed.</param>
/// <returns>The result.</returns>
public sealed record JwtRefreshResult(bool Succeeded, string? Token, DateTime? ExpiresUtc, string? FailureReason)
{
    /// <summary>
    /// Creates a successful refresh result.
    /// </summary>
    /// <param name="token">Renewed JWT.</param>
    /// <param name="expiresUtc">Token expiry in UTC.</param>
    /// <returns>A successful refresh result.</returns>
    public static JwtRefreshResult Success(string token, DateTime expiresUtc)
        => new(true, token, expiresUtc, null);

    /// <summary>
    /// Creates a failed refresh result.
    /// </summary>
    /// <param name="reason">Diagnostic rejection reason.</param>
    /// <returns>A failed refresh result.</returns>
    public static JwtRefreshResult Fail(string reason)
        => new(false, null, null, reason);
}

/// <summary>
/// Default JWT refresh implementation backed by ASP.NET Identity user state and security stamps.
/// </summary>
public sealed class JwtRefreshService : IJwtRefreshService
{
    /// <summary>
    /// Claim type used to bind issued JWTs to the current Identity security stamp.
    /// </summary>
    public const string SecurityStampClaimType = "security_stamp";

    private readonly UserManager<User> _userManager;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<JwtRefreshService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JwtRefreshService"/> class.
    /// </summary>
    /// <param name="userManager">Identity user manager used to load users and roles.</param>
    /// <param name="jwt">JWT token service used to issue renewed tokens.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public JwtRefreshService(UserManager<User> userManager, IJwtTokenService jwt, ILogger<JwtRefreshService> logger)
    {
        _userManager = userManager;
        _jwt = jwt;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JwtRefreshResult> RefreshAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return JwtRefreshResult.Fail("Missing user id");
        }

        var tokenStamp = principal.FindFirstValue(SecurityStampClaimType);
        if (string.IsNullOrWhiteSpace(tokenStamp))
        {
            return JwtRefreshResult.Fail("Missing security stamp");
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return JwtRefreshResult.Fail("User not found");
        }
        if (!user.Active)
        {
            return JwtRefreshResult.Fail("User inactive");
        }

        var currentStamp = await _userManager.GetSecurityStampAsync(user);
        if (!string.Equals(tokenStamp, currentStamp, StringComparison.Ordinal))
        {
            _logger.LogInformation("JWT refresh rejected for user {UserId}: security stamp mismatch", userId);
            return JwtRefreshResult.Fail("Security stamp mismatch");
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
        var token = _jwt.CreateToken(user.Id, user.UserName!, isAdmin, currentStamp, out var expiresUtc, user.PreferredLanguage, user.TimeZoneId);
        return JwtRefreshResult.Success(token, expiresUtc);
    }
}
