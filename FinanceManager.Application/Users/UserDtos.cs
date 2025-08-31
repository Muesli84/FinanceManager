namespace FinanceManager.Application.Users;

public sealed record RegisterUserCommand(string Username, string Password, string? PreferredLanguage);
public sealed record LoginCommand(string Username, string Password);

public sealed record AuthResult(Guid UserId, string Username, bool IsAdmin, string Token, DateTime ExpiresUtc);
