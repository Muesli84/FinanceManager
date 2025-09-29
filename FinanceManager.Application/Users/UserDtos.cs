namespace FinanceManager.Application.Users;

public sealed record RegisterUserCommand(string Username, string Password, string? PreferredLanguage);

public sealed record LoginCommand
{
    public string Username { get; }
    public string Password { get; }
    public string? IpAddress { get; }

    public LoginCommand(string username, string password, string? ipAddress = null)
    {
        Username = username;
        Password = password;
        IpAddress = ipAddress;
    }
}

public sealed record AuthResult(Guid UserId, string Username, bool IsAdmin, string Token, DateTime ExpiresUtc);
