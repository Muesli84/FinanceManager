namespace FinanceManager.Application.Contacts;

/// <summary>
/// Service to manage contact categories for a user.
/// </summary>
public interface IContactCategoryService
{
    /// <summary>
    /// Lists all contact categories for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<IReadOnlyList<ContactCategoryDto>> ListAsync(Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Creates a new contact category for the owner.
    /// </summary>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ContactCategoryDto> CreateAsync(Guid ownerUserId, string name, CancellationToken ct);

    /// <summary>
    /// Sets or clears the symbol attachment for the category.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="attachmentId">The attachment id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetSymbolAttachmentAsync(Guid id, Guid ownerUserId, Guid? attachmentId, CancellationToken ct);

    /// <summary>
    /// Gets a contact category by id or null when not found.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result.</returns>
    Task<ContactCategoryDto?> GetAsync(Guid id, Guid ownerUserId, CancellationToken ct);

    /// <summary>
    /// Updates the name of a contact category.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="name">The name.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(Guid id, Guid ownerUserId, string name, CancellationToken ct);

    /// <summary>
    /// Deletes a contact category.
    /// </summary>
    /// <param name="id">Identifier of the entity.</param>
    /// <param name="ownerUserId">The owner user id.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(Guid id, Guid ownerUserId, CancellationToken ct);
}