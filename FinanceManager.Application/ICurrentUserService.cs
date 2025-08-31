namespace FinanceManager.Application;

public interface ICurrentUserService
{
    Guid UserId { get; }
    string? PreferredLanguage { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
}
