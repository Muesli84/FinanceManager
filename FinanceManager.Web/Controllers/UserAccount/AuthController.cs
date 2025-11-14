using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers.UserAccount;

/// <summary>
/// Authentication endpoints: login, register and logout. Sets auth cookie on successful login/register.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserAuthService _auth;
    private readonly IAuthTokenProvider _tokenProvider;
    private const string AuthCookieName = "FinanceManager.Auth";

    /// <summary>
    /// Creates a new instance of <see cref="AuthController"/>.
    /// </summary>
    /// <param name="auth">User authentication service.</param>
    /// <param name="tokenProvider">Token provider used for cookie-based token management.</param>
    public AuthController(IUserAuthService auth, IAuthTokenProvider tokenProvider)
    { _auth = auth; _tokenProvider = tokenProvider; }

    /// <summary>
    /// Authenticates a user and sets an authentication cookie on success.
    /// </summary>
    /// <param name="request">Login request containing username and password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with user info and token expiry on success; 401 Unauthorized on failure; 400 for validation errors.</returns>
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(new LoginCommand(request.Username, request.Password, ip, request.PreferredLanguage, request.TimeZoneId), ct);
        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }

        // Set cookie with explicit expiry that matches token expiry
        Response.Cookies.Append(AuthCookieName, result.Value!.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = new DateTimeOffset(result.Value.ExpiresUtc)
        });

        return Ok(new { user = result.Value.Username, isAdmin = result.Value.IsAdmin, exp = result.Value.ExpiresUtc });
    }

    /// <summary>
    /// Registers a new user and sets authentication cookie on success.
    /// </summary>
    /// <param name="request">Registration request containing username and password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>200 OK with user info and token expiry on success; 409 Conflict on business errors; 400 for validation errors.</returns>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var result = await _auth.RegisterAsync(new RegisterUserCommand(request.Username, request.Password, request.PreferredLanguage, request.TimeZoneId), ct);
        if (!result.Success)
        {
            return Conflict(new { error = result.Error });
        }

        Response.Cookies.Append(AuthCookieName, result.Value!.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true,
            Expires = new DateTimeOffset(result.Value.ExpiresUtc)
        });

        return Ok(new { user = result.Value.Username, isAdmin = result.Value.IsAdmin, exp = result.Value.ExpiresUtc });
    }

    /// <summary>
    /// Logs out the current user by deleting the auth cookie and clearing token provider state if applicable.
    /// </summary>
    /// <returns>200 OK.</returns>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (Request.Cookies.ContainsKey(AuthCookieName))
        {
            Response.Cookies.Delete(AuthCookieName, new CookieOptions
            {
                Path = "/",
                Secure = Request.IsHttps,
                HttpOnly = true,
                SameSite = SameSiteMode.Lax
            });
        }
        if (_tokenProvider is JwtCookieAuthTokenProvider concrete)
        {
            concrete.Clear();
        }
        return Ok();
    }

    /// <summary>
    /// Login request payload.
    /// </summary>
    public sealed class LoginRequest
    {
        [Required, MinLength(3)]
        public string Username { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        public string? PreferredLanguage { get; set; }
        public string? TimeZoneId { get; set; }
    }

    /// <summary>
    /// Registration request payload.
    /// </summary>
    public sealed class RegisterRequest
    {
        [Required, MinLength(3)]
        public string Username { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
        public string? PreferredLanguage { get; set; }
        public string? TimeZoneId { get; set; }
    }
}

