namespace FinanceManager.Domain.Attachments;

/// <summary>
/// Represents a user-defined attachment category used to classify attachments.
/// </summary>
public sealed class AttachmentCategory
{
    /// <summary>
    /// Unique category identifier.
    /// </summary>
    /// <returns>The result.</returns>
    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Owner user identifier who created the category.
    /// </summary>
    public Guid OwnerUserId { get; private set; }

    /// <summary>
    /// Display name of the category.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// When true, the category is a system category and cannot be deleted by normal users.
    /// </summary>
    public bool IsSystem { get; private set; }

    /// <summary>
    /// Creates a new attachment category for the given owner.
    /// </summary>
    /// <param name="ownerUserId">Id of the user who owns the category.</param>
    /// <param name="name">Display name of the category.</param>
    /// <param name="isSystem">When true, marks the category as system-owned (protected).</param>
    public AttachmentCategory(Guid ownerUserId, string name, bool isSystem = false)
    {
        OwnerUserId = ownerUserId;
        Rename(name);
        IsSystem = isSystem;
    }

    /// <summary>
    /// Renames the category.
    /// </summary>
    /// <param name="name">New display name.</param>
    /// <exception cref="ArgumentException">Thrown when the provided name is null, empty, or consists only of whitespace characters.</exception>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("Name required", nameof(name)); }
        Name = name.Trim();
    }

    // Backup DTO
    /// <summary>
    /// DTO carrying the serializable state of an <see cref="AttachmentCategory"/> used for backups.
    /// </summary>
    /// <param name="Id">Identifier of the category entity.</param>
    /// <param name="OwnerUserId">Identifier of the user who owns the category.</param>
    /// <param name="Name">Display name of the category.</param>
    /// <param name="IsSystem">Flag indicating whether the category is a system category.</param>
    /// <returns>The result.</returns>
    public sealed record AttachmentCategoryBackupDto(Guid Id, Guid OwnerUserId, string Name, bool IsSystem);

    /// <summary>
    /// Converts this AttachmentCategory to a backup DTO.
    /// </summary>
    /// <returns>A <see cref="AttachmentCategoryBackupDto"/> containing the data required to restore this category.</returns>
    public AttachmentCategoryBackupDto ToBackupDto()
        => new AttachmentCategoryBackupDto(Id, OwnerUserId, Name, IsSystem);

    /// <summary>
    /// Assigns values from a backup DTO to this entity.
    /// </summary>
    /// <param name="dto">The <see cref="AttachmentCategoryBackupDto"/> containing values to apply to this entity.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dto"/> is <c>null</c>.</exception>
    public void AssignBackupDto(AttachmentCategoryBackupDto dto)
    {
        if (dto == null) throw new ArgumentNullException(nameof(dto));
        Rename(dto.Name);
        IsSystem = dto.IsSystem;
    }
}
