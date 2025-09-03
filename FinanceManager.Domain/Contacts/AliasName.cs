namespace FinanceManager.Domain.Contacts;

public sealed class AliasName : Entity
{
    private AliasName() { }
    public AliasName(Guid contactId, string pattern)
    {
        ContactId = Guards.NotEmpty(contactId, nameof(contactId));
        Pattern = Guards.NotNullOrWhiteSpace(pattern, nameof(pattern));
    }
    public Guid ContactId { get; private set; }
    public string Pattern { get; private set; } = null!;

    public void ReassignTo(Guid newContactId)
    {
        ContactId = newContactId;
    }
}