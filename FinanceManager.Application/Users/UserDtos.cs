namespace FinanceManager.Application.Users;

public sealed record RegisterUserCommand(string Username, string Password, string? PreferredLanguage, string? TimeZoneId);

public sealed record LoginCommand
{
    public string Username { get; }
    public string Password { get; }
    public string? IpAddress { get; }
    public string? PreferredLanguage { get; }
    public string? TimeZoneId { get; }

    public LoginCommand(string username, string password, string? ipAddress = null, string? preferredLanguage = null, string? timeZoneId = null)
    {
        Username = username;
        Password = password;
        IpAddress = ipAddress;
        PreferredLanguage = preferredLanguage;
        TimeZoneId = timeZoneId;
    }
}

public sealed record AuthResult(Guid UserId, string Username, bool IsAdmin, string Token, DateTime ExpiresUtc);
