using System;

namespace FinanceManager.Domain.Attachments;

public sealed class AttachmentCategory
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }

    private AttachmentCategory() { }

    public AttachmentCategory(Guid ownerUserId, string name, bool isSystem = false)
    {
        OwnerUserId = ownerUserId;
        Rename(name);
        IsSystem = isSystem;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("Name required", nameof(name)); }
        Name = name.Trim();
    }
}
