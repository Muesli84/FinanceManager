namespace FinanceManager.Application.Attachments;

/// <summary>
/// Service to manage attachment categories for a user (list, create, update, delete).
/// </summary>
public interface IAttachmentCategoryService
{
    /// <summary>
    /// Lists all attachment categories for the owner.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Read-only list of attachment category DTOs.</returns>
    Task<IReadOnlyList<AttachmentCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Creates a new attachment category.
    /// </summary>
    /// <param name="ownerUserId">Owner user id.</param>
    /// <param name="name">Category name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Created attachment category DTO.</returns>
    Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);

    /// <summary>
    /// Creates a system category (protected) for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="isSystem">The is system.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AttachmentCategoryDto> CreateAsync(Guid ownerUserId, string name, bool isSystem, CancellationToken ct);

    /// <summary>
    /// Deletes a category if allowed.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<bool> DeleteAsync(Guid ownerUserId, Guid id, CancellationToken ct);

    /// <summary>
    /// Updates the name of a category. Returns the updated DTO or null when not found.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<AttachmentCategoryDto?> UpdateAsync(Guid ownerUserId, Guid id, string name, CancellationToken ct);
}
