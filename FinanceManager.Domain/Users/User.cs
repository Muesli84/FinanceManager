namespace FinanceManager.Domain.Users;

public sealed class User : Entity, IAggregateRoot
{
    private User() { }
    public User(string username, string passwordHash, bool isAdmin)
    {
        Username = Guards.NotNullOrWhiteSpace(username, nameof(username));
        PasswordHash = Guards.NotNullOrWhiteSpace(passwordHash, nameof(passwordHash));
        IsAdmin = isAdmin;
    }

    public string Username { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsAdmin { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public DateTime LastLoginUtc { get; private set; }
    public bool Active { get; private set; } = true;

    public void MarkLogin(DateTime utcNow) => LastLoginUtc = utcNow;
    public void SetLockedUntil(DateTime? utc) => LockedUntilUtc = utc;
    public void SetPreferredLanguage(string? lang) => PreferredLanguage = lang;
    public void Deactivate() => Active = false;
}