namespace FinanceManager.Domain.Contacts;

/// <summary>
/// Domain entity representing a contact belonging to a user.
/// Stores metadata like name, type, category and optional symbol attachment.
/// </summary>
public sealed class Contact : Entity, IAggregateRoot
{
    private Contact() { }
    /// <summary>
    /// Creates a new contact for the given owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user identifier.</param>
    /// <param name="name">Display name of the contact.</param>
    /// <param name="type">Contact type.</param>
    /// <param name="categoryId">Optional category id.</param>
    /// <param name="description">Optional description text.</param>
    /// <param name="isPaymentIntermediary">Optional flag indicating payment intermediary.</param>
    public Contact(Guid ownerUserId, string name, ContactType type, Guid? categoryId, string? description = null, bool? isPaymentIntermediary = false)
    {
        OwnerUserId = Guards.NotEmpty(ownerUserId, nameof(ownerUserId));
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Type = type;
        CategoryId = categoryId;
        Description = description;
        IsPaymentIntermediary = isPaymentIntermediary ?? false;
    }

    /// <summary>
    /// Identifier of the user who owns the contact.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Display name of the contact.
    /// </summary>
    public string Name { get; private set; } = null!;

    /// <summary>
    /// Type/category of the contact.
    /// </summary>
    public ContactType Type { get; private set; }

    /// <summary>
    /// Optional category id the contact belongs to.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>
    /// Optional description text for the contact.
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Flag indicating whether this contact is used as a payment intermediary.
    /// </summary>
    public bool IsPaymentIntermediary { get; private set; }

    /// <summary>
    /// Optional symbol attachment id associated with the contact.
    /// </summary>
    public Guid? SymbolAttachmentId { get; private set; }

    /// <summary>
    /// Renames the contact.
    /// </summary>
    /// <param name="name">New display name.</param>
    public void Rename(string name)
    {
        Name = Guards.NotNullOrWhiteSpace(name, nameof(name));
        Touch();
    }

    /// <summary>
    /// Changes the contact type.
    /// </summary>
    /// <param name="type">New contact type.</param>
    public void ChangeType(ContactType type)
    {
        Type = type;
        Touch();
    }

    /// <summary>
    /// Sets or clears the contact category.
    /// </summary>
    /// <param name="categoryId">Category id or null to clear.</param>
    public void SetCategory(Guid? categoryId)
    {
        CategoryId = categoryId;
        Touch();
    }

    /// <summary>
    /// Sets the description text for the contact.
    /// </summary>
    /// <param name="description">Description text or null to clear.</param>
    public void SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    /// <summary>
    /// Sets whether the contact is a payment intermediary.
    /// </summary>
    /// <param name="value">Boolean flag value.</param>
    public void SetPaymentIntermediary(bool value)
    {
        IsPaymentIntermediary = value;
        Touch();
    }

    /// <summary>
    /// Sets or clears the symbol attachment for the contact.
    /// </summary>
    /// <param name="attachmentId">Attachment id or null to clear.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
        Touch();
    }

    /// <summary>
    /// DTO used for backups representing the serializable state of the contact.
    /// </summary>
    /// <param name="Id">Identifier of the entity.</param>
    /// <param name="OwnerUserId">The owner user id.</param>
    /// <param name="Name">The name.</param>
    /// <param name="Type">The type.</param>
    /// <param name="CategoryId">The category id.</param>
    /// <param name="Description">The description.</param>
    /// <param name="IsPaymentIntermediary">The is payment intermediary.</param>
    /// <param name="SymbolAttachmentId">The symbol attachment id.</param>
    /// <returns>The result.</returns>
    public sealed record ContactBackupDto(Guid Id, Guid OwnerUserId, string Name, ContactType Type, Guid? CategoryId, string? Description, bool IsPaymentIntermediary, Guid? SymbolAttachmentId);

    /// <summary>
    /// Creates a backup DTO from the current entity state.
    /// </summary>
    /// <returns>A <see cref="ContactBackupDto"/> representing the serializable state of this contact.</returns>
    public ContactBackupDto ToBackupDto()
    {
        return new ContactBackupDto(Id, OwnerUserId, Name, Type, CategoryId, Description, IsPaymentIntermediary, SymbolAttachmentId);
    }

    /// <summary>
    /// Assigns values from a backup DTO to this entity. Uses entity setters to preserve invariants.
    /// </summary>
    /// <param name="dto">Backup DTO to read values from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(ContactBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        // Id is intentionally not assigned here; identity handled by repository/ORM.
        Rename(dto.Name);
        ChangeType(dto.Type);
        SetCategory(dto.CategoryId);
        SetDescription(dto.Description);
        SetPaymentIntermediary(dto.IsPaymentIntermediary);
        SetSymbolAttachment(dto.SymbolAttachmentId);
    }
}