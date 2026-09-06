namespace FinanceManager.Domain.Contacts;

/// <summary>
/// Category used to group contacts for the user.
/// </summary>
public sealed class ContactCategory : Entity, IAggregateRoot
{
    private ContactCategory() { }

    /// <summary>
    /// Creates a new contact category for the specified owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="name">Category display name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ownerUserId"/> is an empty GUID or <paramref name="name"/> is null/whitespace.</exception>
    public ContactCategory(Guid ownerUserId, string name)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
    }

    /// <summary>
    /// Owner user identifier.
    /// </summary>
    /// <value>The GUID of the owning user.</value>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Category display name.
    /// </summary>
    /// <value>The display name shown in UI lists.</value>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Optional symbol attachment id for the category.
    /// </summary>
    /// <value>The attachment identifier used as a symbol, or <c>null</c> when none is set.</value>
    public Guid? SymbolAttachmentId { get; private set; }

    /// <summary>
    /// Renames the contact category.
    /// </summary>
    /// <param name="name">New display name.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty or whitespace.</exception>
    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    /// <summary>
    /// Sets or clears the symbol attachment for the category.
    /// </summary>
    /// <param name="attachmentId">Attachment id or null to clear. Passing <see cref="Guid.Empty"/> is treated as clearing.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="ContactCategory"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Entity identifier.</param>
    /// <param name="OwnerUserId">Owner user identifier.</param>
    /// <param name="Name">Category display name.</param>
    /// <param name="SymbolAttachmentId">Optional symbol attachment id.</param>
    /// <returns>The result.</returns>
    public sealed record ContactCategoryBackupDto(Guid Id, Guid OwnerUserId, string Name, Guid? SymbolAttachmentId);

    /// <summary>
    /// Converts this ContactCategory to a backup DTO.
    /// </summary>
    /// <returns>A <see cref="ContactCategoryBackupDto"/> representing this category.</returns>
    public ContactCategoryBackupDto ToBackupDto()
        => new ContactCategoryBackupDto(Id, OwnerUserId, Name, SymbolAttachmentId);

    /// <summary>
    /// Assigns values from a backup DTO to this entity.
    /// </summary>
    /// <param name="dto">Backup DTO to apply.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(ContactCategoryBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Rename(dto.Name);
        SetSymbolAttachment(dto.SymbolAttachmentId);
    }
}
