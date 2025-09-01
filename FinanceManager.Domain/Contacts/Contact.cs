namespace FinanceManager.Domain.Contacts;

public sealed class Contact : Entity, IAggregateRoot
{
    private Contact() { }
    public Contact(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description = null)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Type = type;
        CategoryId = categoryId;
        Description = description;
    }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = null!;
    public ContactType Type { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string? Description { get; private set; }

    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    public void ChangeType(ContactType type)
    {
        Type = type;
        Touch();
    }

    public void SetCategory(Guid? categoryId)
    {
        CategoryId = categoryId;
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }
}