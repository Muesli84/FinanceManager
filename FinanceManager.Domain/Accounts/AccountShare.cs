namespace FinanceManager.Domain.Accounts;

public sealed class AccountShare : Entity
{
    private AccountShare() { }
    public AccountShare(Guid accountId, Guid userId, AccountShareRole role)
    {
        AccountId = Guards.NotEmpty(accountId, nameof(accountId));
        UserId = Guards.NotEmpty(userId, nameof(userId));
        Role = role;
    }
    public Guid AccountId { get; private set; }
    public Guid UserId { get; private set; }
    public AccountShareRole Role { get; private set; }
    public DateTime GrantedUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? RevokedUtc { get; private set; }
}