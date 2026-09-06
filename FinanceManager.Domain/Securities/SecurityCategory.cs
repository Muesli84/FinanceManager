namespace FinanceManager.Domain.Securities;

/// <summary>
/// Represents a user-defined category for securities. Categories can have an optional symbol attachment.
/// </summary>
public sealed class SecurityCategory
{
    /// <summary>
    /// Gets the identifier of the category.
    /// </summary>
    /// <value>The category GUID.</value>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the identifier of the user who owns this category.
    /// </summary>
    /// <value>The owner user's GUID.</value>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Gets the display name of the category.
    /// </summary>
    /// <value>The category name.</value>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Optional reference to a symbol attachment associated with this category.
    /// Passing <see cref="Guid.Empty"/> when setting is treated as clearing the attachment (null).
    /// </summary>
    /// <value>The attachment GUID or <c>null</c>.</value>
    public Guid? SymbolAttachmentId { get; private set; }

    /// <summary>
    /// Parameterless constructor for ORM/deserialization.
    /// </summary>
    private SecurityCategory() { }

    /// <summary>
    /// Creates a new <see cref="SecurityCategory"/> for the specified owner with the given name.
    /// </summary>
    /// <param name="ownerUserId">The identifier of the user who owns this category.</param>
    /// <param name="name">The display name of the category. Must not be null or whitespace.</param>
    public SecurityCategory(Guid ownerUserId, string name)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Rename(name);
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    /// <param name="name">New name for the category. Must not be null or whitespace.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null, empty or consists only of white-space characters.</exception>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name required", nameof(name));
        }
        Name = name.Trim();
    }

    /// <summary>
    /// Sets or clears the symbol attachment reference for this category.
    /// </summary>
    /// <param name="attachmentId">Attachment GUID to set, or <see cref="Guid.Empty"/>/<c>null</c> to clear.</param>
    public void SetSymbolAttachment(Guid? attachmentId)
    {
        SymbolAttachmentId = attachmentId == Guid.Empty ? null : attachmentId;
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of a <see cref="SecurityCategory"/> for backup purposes.
    /// </summary>
    /// <param name="Id">Identifier of the category entity.</param>
    /// <param name="OwnerUserId">Identifier of the user who owns the category.</param>
    /// <param name="Name">Display name of the category.</param>
    /// <param name="SymbolAttachmentId">Optional symbol attachment identifier associated with the category.</param>
    /// <returns>The result.</returns>
    public sealed record SecurityCategoryBackupDto(Guid Id, Guid OwnerUserId, string Name, Guid? SymbolAttachmentId);

    /// <summary>
    /// Creates a backup DTO for this security category.
    /// </summary>
    /// <returns>A <see cref="SecurityCategoryBackupDto"/> containing the data required to restore this category.</returns>
    public SecurityCategoryBackupDto ToBackupDto()
        => new SecurityCategoryBackupDto(Id, OwnerUserId, Name, SymbolAttachmentId);

    /// <summary>
    /// Assigns values from a backup DTO to this category.
    /// </summary>
    /// <param name="dto">The <see cref="SecurityCategoryBackupDto"/> containing values to apply to this entity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(SecurityCategoryBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Rename(dto.Name);
        SetSymbolAttachment(dto.SymbolAttachmentId);
    }
}
