namespace FinanceManager.Domain.Contacts;

public sealed class ContactCategory : Entity, IAggregateRoot
{
    private ContactCategory() { }

    public ContactCategory(Guid ownerUserId, string name)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
    }

    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = null!;

    // Optional symbol attachment for category
    public Guid? SymbolAttachmentId { get; private set; }

    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }
}