namespace FinanceManager.Application.Users;

/// <summary>
/// Command used to register a new user with username and password and optional preferences.
/// </summary>
/// <param name="Username">Desired username for the new account.</param>
/// <param name="Password">Plain-text password provided by the user (should be validated and hashed by the caller/service).</param>
/// <param name="PreferredLanguage">Optional preferred UI language tag (e.g. "en", "de").</param>
/// <param name="TimeZoneId">Optional IANA time zone identifier (e.g. "Europe/Berlin").</param>
/// <returns>The result.</returns>
public sealed record RegisterUserCommand(string Username, string Password, string? PreferredLanguage, string? TimeZoneId);

/// <summary>
/// Command used to authenticate a user with username and password and optional context information.
/// </summary>
public sealed record LoginCommand
{
    /// <summary>
    /// Username provided for authentication.
    /// </summary>
    public string Username { get; }

    /// <summary>
    /// Plain-text password provided for authentication.
    /// </summary>
    public string Password { get; }

    /// <summary>
    /// Optional IP address of the client performing the login.
    /// </summary>
    public string? IpAddress { get; }

    /// <summary>
    /// Optional preferred language reported by the client at login time.
    /// </summary>
    public string? PreferredLanguage { get; }

    /// <summary>
    /// Optional IANA time zone identifier reported by the client at login time.
    /// </summary>
    public string? TimeZoneId { get; }

    /// <summary>
    /// Creates a new <see cref="LoginCommand"/> instance.
    /// </summary>
    /// <param name="username">Username for authentication.</param>
    /// <param name="password">Password for authentication.</param>
    /// <param name="ipAddress">Optional client IP address.</param>
    /// <param name="preferredLanguage">Optional preferred language reported by the client.</param>
    /// <param name="timeZoneId">Optional IANA time zone identifier reported by the client.</param>
    public LoginCommand(string username, string password, string? ipAddress = null, string? preferredLanguage = null, string? timeZoneId = null)
    {
        Username = username;
        Password = password;
        IpAddress = ipAddress;
        PreferredLanguage = preferredLanguage;
        TimeZoneId = timeZoneId;
    }
}

/// <summary>
/// Result returned after successful authentication containing identifying information and a token.
/// </summary>
/// <param name="UserId">Identifier of the authenticated user.</param>
/// <param name="Username">Username of the authenticated user.</param>
/// <param name="IsAdmin">Whether the authenticated user has administrative privileges.</param>
/// <param name="Token">Authentication token (e.g. JWT) to be used for subsequent requests.</param>
/// <param name="ExpiresUtc">UTC expiration time of the token.</param>
/// <returns>The result.</returns>
public sealed record AuthResult(Guid UserId, string Username, bool IsAdmin, string Token, DateTime ExpiresUtc);
