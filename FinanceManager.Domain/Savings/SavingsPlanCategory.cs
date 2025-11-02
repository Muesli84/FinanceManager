using System;

namespace FinanceManager.Domain.Savings;

public sealed class SavingsPlanCategory
{
    public Guid Id { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; }

    // Optional symbol attachment for category
    public Guid? SymbolAttachmentId { get; private set; }

    public SavingsPlanCategory(Guid ownerUserId, string name)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Name = name;
    }

    public void Rename(string name) => Name = name;

    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
    }
}