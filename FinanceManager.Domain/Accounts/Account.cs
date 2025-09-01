namespace FinanceManager.Domain.Accounts;

public sealed class Account : Entity, IAggregateRoot
{
    private Account() { }
    public Account(Guid ownerUserId, AccountType type, string name, string? iban, Guid bankContactId)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Type = type;
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Iban = iban?.Trim();
        BankContactId = Guards.NotEmpty(bankContactId, nameof(bankContactId));
    }
    public Guid OwnerUserId { get; private set; }
    public AccountType Type { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Iban { get; private set; }
    public decimal CurrentBalance { get; private set; }
    public Guid BankContactId { get; private set; }

    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    public void SetIban(string? iban)
    {
        Iban = string.IsNullOrWhiteSpace(iban) ? null : iban.Trim();
        Touch();
    }

    public void SetBankContact(Guid bankContactId)
    {
        BankContactId = Guards.NotEmpty(bankContactId, nameof(bankContactId));
        Touch();
    }

    public void SetType(AccountType type)
    {
        if (Type != type)
        {
            Type = type;
            Touch();
        }
    }

    public void AdjustBalance(decimal delta)
    {
        CurrentBalance += delta;
        Touch();
    }
}