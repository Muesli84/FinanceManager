using System;

namespace FinanceManager.Domain.Securities;

public sealed class SecurityCategory
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    // Optional symbol attachment for category
    public Guid? SymbolAttachmentId { get; private set; }

    private SecurityCategory() { }

    public SecurityCategory(Guid ownerUserId, string name)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Rename(name);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name required", nameof(name));
        }
        Name = name.Trim();
    }

    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
    }
}