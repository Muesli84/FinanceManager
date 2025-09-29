using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FinanceManager.Application.Users;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManager.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserAuthService _auth;
    private readonly IAuthTokenProvider _tokenProvider;

    public AuthController(IUserAuthService auth, IAuthTokenProvider tokenProvider)
    { _auth = auth; _tokenProvider = tokenProvider; }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] LoginRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _auth.LoginAsync(new LoginCommand(request.Username, request.Password, ip), ct);
        if (!result.Success)
        {
            return Unauthorized(new { error = result.Error });
        }
        Response.Cookies.Append("fm_auth", result.Value!.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps, // wichtig für HTTP-only
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true
        });
        return Ok(new { user = result.Value.Username, isAdmin = result.Value.IsAdmin, exp = result.Value.ExpiresUtc });
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAsync([FromBody] RegisterRequest request, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }
        var result = await _auth.RegisterAsync(new RegisterUserCommand(request.Username, request.Password, null), ct);
        if (!result.Success)
        {
            return Conflict(new { error = result.Error });
        }
        Response.Cookies.Append("fm_auth", result.Value!.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            IsEssential = true
        });
        return Ok(new { user = result.Value.Username, isAdmin = result.Value.IsAdmin, exp = result.Value.ExpiresUtc });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        if (Request.Cookies.ContainsKey("fm_auth"))
        {
            Response.Cookies.Delete("fm_auth", new CookieOptions
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

    public sealed class LoginRequest
    {
        [Required, MinLength(3)]
        public string Username { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }

    public sealed class RegisterRequest
    {
        [Required, MinLength(3)]
        public string Username { get; set; } = string.Empty;
        [Required, MinLength(6)]
        public string Password { get; set; } = string.Empty;
    }
}
